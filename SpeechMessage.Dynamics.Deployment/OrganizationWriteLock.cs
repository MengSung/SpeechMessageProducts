/*
 * 檔案整體說明
 *
 * 目的與定位：以本機檔案控制代碼互斥同一組織的寫入命令。租約只在命令期間持有，程序結束或 Dispose 後由作業系統釋放，不保存組織資料、帳密或跨命令可變狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 OrganizationWriteLock。
 * - 取得指定組織輸出根的獨占寫入租約；等待可由 caller 取消，超過上限時以固定訊息失敗。
 * - 需要電腦級互斥時，先取得工作樹本地鎖，再取得共享鎖目錄內的組織鎖；兩把鎖都由本次租約唯一擁有。
 * - Dispose 以 shared -> local 的反向順序釋放所有控制代碼；任何取得失敗都在 finally 釋放部分租約，再決定是否重試。
 *   空鎖檔留在 artifacts 供後續命令重用；互斥所有權只由可確定釋放的控制代碼代表。
 * 主要操作入口包含 Acquire、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 以本機檔案控制代碼互斥同一組織的寫入命令。租約只在命令期間持有，程序結束或
/// Dispose 後由作業系統釋放，不保存組織資料、帳密或跨命令可變狀態。
/// </summary>
internal sealed class OrganizationWriteLock : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);
    private FileStream? lockStream;
    private FileStream? sharedLockStream;

    private OrganizationWriteLock(FileStream lockStream, FileStream? sharedLockStream)
    {
        this.lockStream = lockStream;
        this.sharedLockStream = sharedLockStream;
    }

    /// <summary>
    /// 取得指定組織輸出根的獨占寫入租約；等待可由 caller 取消，超過上限時以固定訊息失敗。
    /// </summary>
    /// <param name="organizationRoot">唯一白名單組織的本機 artifacts 根目錄。</param>
    /// <param name="timeout">等待其他寫入命令釋放鎖的時間上限。</param>
    /// <param name="cancellationToken">呼叫端取消等待的訊號；不保留至租約生命週期。</param>
    /// <returns>由目前命令唯一持有的互斥租約。</returns>
    public static OrganizationWriteLock Acquire(
        string organizationRoot,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => AcquireCore(organizationRoot, timeout, null, cancellationToken);

    /// <summary>
    /// 取得整台電腦共用的組織鎖；此 overload 將 sharedLockDirectory 放在具名參數，
    /// 同時保留既有呼叫端的 CancellationToken 參數順序，避免生命週期語意被錯綁。
    /// </summary>
    /// <param name="organizationRoot">唯一白名單組織的本機 artifacts 根目錄。</param>
    /// <param name="timeout">等待其他寫入命令釋放鎖的時間上限。</param>
    /// <param name="sharedLockDirectory">整台電腦共用的鎖目錄。</param>
    /// <param name="cancellationToken">呼叫端取消訊號。</param>
    public static OrganizationWriteLock Acquire(
        string organizationRoot,
        TimeSpan timeout,
        string sharedLockDirectory,
        CancellationToken cancellationToken = default)
        => AcquireCore(organizationRoot, timeout, sharedLockDirectory, cancellationToken);

    private static OrganizationWriteLock AcquireCore(
        string organizationRoot,
        TimeSpan timeout,
        string? sharedLockDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(organizationRoot);
        Directory.CreateDirectory(root);
        var localLockPath = Path.Combine(root, ".deployment-write.lock");
        var organizationName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(organizationName))
            throw new ArgumentException("Organization root must identify an organization.", nameof(organizationRoot));
        var sharedLockPath = string.IsNullOrWhiteSpace(sharedLockDirectory)
            ? null
            : Path.Combine(Path.GetFullPath(sharedLockDirectory), organizationName + ".lock");
        if (sharedLockPath is not null)
            Directory.CreateDirectory(Path.GetDirectoryName(sharedLockPath)!);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                FileStream? localStream = null;
                FileStream? sharedStream = null;
                try
                {
                    // 所有呼叫端固定 local -> shared 的順序，避免兩個工作樹以相反順序互相等待。
                    localStream = new FileStream(
                        localLockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.None);
                    if (sharedLockPath is not null)
                    {
                        sharedStream = new FileStream(
                            sharedLockPath,
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.None,
                            bufferSize: 1,
                            FileOptions.None);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // 完整取得後才轉移給唯一租約 owner；建構或取消失敗仍由本層 finally 清理。
                    var acquired = new OrganizationWriteLock(localStream, sharedStream);
                    localStream = null;
                    sharedStream = null;
                    return acquired;
                }
                finally
                {
                    // 非 IOException（例如共享位置存取遭拒）也不能留下先取得的本地鎖。
                    // 即使第一個 Dispose 失敗，也必須嘗試釋放最後的本地控制代碼。
                    try { sharedStream?.Dispose(); }
                    finally { localStream?.Dispose(); }
                }
            }
            catch (IOException)
            {
                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException("同一組織的佈署寫入仍在執行，請稍後重試。");
                }

                var wait = remaining < RetryInterval ? remaining : RetryInterval;
                if (cancellationToken.WaitHandle.WaitOne(wait))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
    }

    /// <summary>
    /// 釋放獨占檔案控制代碼，讓下一個命令可以取得同一組織的寫入租約。
    /// 空鎖檔留在 artifacts 供後續命令重用；互斥所有權只由可確定釋放的控制代碼代表。
    /// </summary>
    public void Dispose()
    {
        try { Interlocked.Exchange(ref sharedLockStream, null)?.Dispose(); }
        finally { Interlocked.Exchange(ref lockStream, null)?.Dispose(); }
    }
}
