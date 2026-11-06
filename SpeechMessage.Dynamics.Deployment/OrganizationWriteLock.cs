/*
 * 檔案整體說明
 *
 * 目的與定位：以本機檔案控制代碼互斥同一組織的寫入命令。租約只在命令期間持有，程序結束或 Dispose 後由作業系統釋放，不保存組織資料、帳密或跨命令可變狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 OrganizationWriteLock。
 * - 取得指定組織輸出根的獨占寫入租約；等待可由 caller 取消，超過上限時以固定訊息失敗。
 * - 釋放獨占檔案控制代碼，讓下一個命令可以取得同一組織的寫入租約。 空鎖檔留在 artifacts 供後續命令重用；互斥所有權只由可確定釋放的控制代碼代表。
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

    private OrganizationWriteLock(FileStream lockStream)
    {
        this.lockStream = lockStream;
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(organizationRoot);
        Directory.CreateDirectory(root);
        var lockPath = Path.Combine(root, ".deployment-write.lock");
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                if (cancellationToken.IsCancellationRequested)
                {
                    stream.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return new OrganizationWriteLock(stream);
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
        Interlocked.Exchange(ref lockStream, null)?.Dispose();
    }
}
