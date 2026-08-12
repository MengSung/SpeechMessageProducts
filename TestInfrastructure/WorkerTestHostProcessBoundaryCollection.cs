using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SpeechMessage.Testing;

/// <summary>
/// 定義所有會建立 <c>WorkerTestHost</c>，以及所有必須證明「沒有建立 WorkerTestHost」之測試共用的
/// xUnit collection 名稱。collection fixture 會由每個 test assembly 各自建立，但兩者都取得相同的
/// OS-level lease，因此不會把另一個 testhost 合法建立的 worker 誤判為本測試產品建立的程序。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerTestHostProcessBoundaryCollection : ICollectionFixture<WorkerTestHostProcessBoundaryLease>
{
    /// <summary>
    /// 跨程序集固定 collection identity；此值只描述 test-only process-boundary 類別，沒有 profile、
    /// 使用者、endpoint、credential、CRM payload 或其他可變 session 資料。
    /// </summary>
    public const string Name = "WorkerTestHost process boundary";
}

/// <summary>
/// 以同一使用者 session 可見、無內容的 temporary lock file，獨占 WorkerTestHost process-boundary
/// 測試的 class-collection lifetime。唯一資源 owner 是此 fixture 的 <see cref="FileStream"/>；它不保留
/// process identity、測試資料、principal、cookie、profile 或任何跨測試可變狀態。fixture Dispose 或
/// testhost 終止後，FileStream／OS handle 都會被確定釋放，因此後續 testhost 不會永久被 poisoned。
/// </summary>
public sealed class WorkerTestHostProcessBoundaryLease : IDisposable
{
    private const string LockFilePrefix = "speechmessage-worker-testhost-process-boundary-v1-";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromMilliseconds(50);

    private FileStream? _stream;
    private int _disposed;

    /// <summary>
    /// 由 xUnit collection fixture 建立並立即取得固定的跨程序集 lease。若另一個 worker-boundary test
    /// 未在固定期限內清理，其 class 不得繼續執行，因為任何 process snapshot 都將不再能正確判斷 owner。
    /// </summary>
    public WorkerTestHostProcessBoundaryLease()
        : this(Acquire(DefaultLockPath, DefaultTimeout, DefaultRetryInterval))
    {
    }

    private WorkerTestHostProcessBoundaryLease(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// 釋放唯一持有的 OS file handle。此方法可重複呼叫且不保存 static handle；若 testhost 非正常終止，
    /// Windows 同樣會關閉 process-owned handle。固定 lock file 可保留為零內容名稱節點，但不會代表已持有
    /// lease，也不包含任何測試或產品資料。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _stream, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 僅供 test contract 以唯一 temporary path 驗證排他、deadline 與 disposal release；正式 fixture 一律
    /// 使用固定且無內容的 <see cref="DefaultLockPath"/>，不接受呼叫端輸入作為 production routing authority。
    /// </summary>
    internal static WorkerTestHostProcessBoundaryLease AcquireForTesting(
        string path,
        TimeSpan timeout,
        TimeSpan retryInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateTiming(timeout, retryInterval);
        return new WorkerTestHostProcessBoundaryLease(Acquire(path, timeout, retryInterval));
    }

    /// <summary>
    /// 在單一 bounded deadline 內嘗試以 <see cref="FileShare.None"/> 開啟 zero-content lock file。僅預期的
    /// sharing <see cref="IOException"/> 可以輪詢；權限、路徑、磁碟或其他異常一律直接失敗，避免把未知
    /// resource state 當成安全的程序隔離。沒有背景 task、timer、subscription 或 retry state 被保留。
    /// </summary>
    private static FileStream Acquire(string path, TimeSpan timeout, TimeSpan retryInterval)
    {
        ValidateTiming(timeout, retryInterval);
        var elapsed = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException exception) when (
                IsExpectedContention(exception) && elapsed.Elapsed < timeout)
            {
                var remaining = timeout - elapsed.Elapsed;
                Thread.Sleep(remaining < retryInterval ? remaining : retryInterval);
            }
            catch (IOException exception) when (IsExpectedContention(exception))
            {
                throw new TimeoutException(
                    "The WorkerTestHost process-boundary test lease could not be acquired before the bounded deadline.");
            }
        }
    }

    /// <summary>
    /// 驗證 bounded lease 的時間輸入，防止零／負值造成 busy loop、無上限等待或跨 testhost 的不可預期
    /// contention。這些值只由 fixture 常數與 contract test 提供，不能承載產品 session 或部署設定。
    /// </summary>
    private static void ValidateTiming(TimeSpan timeout, TimeSpan retryInterval)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (retryInterval <= TimeSpan.Zero || retryInterval > timeout)
        {
            throw new ArgumentOutOfRangeException(nameof(retryInterval));
        }
    }

    /// <summary>
    /// 只有 Windows 的 sharing violation（32）與 lock violation（33）代表另一個 testhost 正持有同一個
    /// lease，才允許 bounded polling。任何其他 <see cref="IOException"/> 都是檔案系統、路徑或環境 defect，
    /// 必須立即原樣失敗，不能被誤稱為安全的 contention timeout。
    /// </summary>
    private static bool IsExpectedContention(IOException exception)
    {
        const int SharingViolation = 32;
        const int LockViolation = 33;
        var win32ErrorCode = exception.HResult & 0xFFFF;
        return win32ErrorCode is SharingViolation or LockViolation;
    }

    /// <summary>
    /// 固定在目前 Windows user temporary directory 的無內容 coordination artifact。它不寫入任何資料，且
    /// 不使用 Global namespace，因此不會跨使用者 session 建立不必要的測試協調或泄漏邊界。
    /// </summary>
    private static string DefaultLockPath => BuildLockPathForTesting(
        Path.GetTempPath(),
        FindSolutionRoot());

    /// <summary>
    /// 由 canonical solution root 產生不可逆 SHA-256 path partition。相同 worktree 的兩個 testhost
    /// 會取得同一把 lock；不同 checkout／worktree 則不會因固定 TEMP 檔名互相阻塞。產出檔名不包含
    /// 原始路徑、使用者資料、profile、endpoint 或 credential，避免 temporary artifact 暴露本機環境資訊。
    /// </summary>
    internal static string BuildLockPathForTesting(string temporaryDirectory, string solutionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionRoot);
        var canonicalRoot = Path.GetFullPath(solutionRoot).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRoot)))
            .ToLowerInvariant();
        return Path.Combine(temporaryDirectory, LockFilePrefix + hash[..16] + ".lock");
    }

    /// <summary>
    /// 從目前 testhost 的 base directory 向上尋找 solution marker。這個 discovery 只決定 test-only
    /// lock partition，不會讀取組態、部署資料或任何使用者內容；找不到 marker 時立即 fail closed，
    /// 不能退回到跨 worktree 共用的固定檔名。
    /// </summary>
    private static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "The WorkerTestHost process-boundary test lease could not locate the solution root.");
    }
}
