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
