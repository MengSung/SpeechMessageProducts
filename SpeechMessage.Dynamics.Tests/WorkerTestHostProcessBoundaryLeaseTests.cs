using FluentAssertions;
using SpeechMessage.Testing;
using Xunit;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 WorkerTestHost 測試程序邊界 lease 的跨程序集互斥與釋放契約。
/// 測試先於 shared helper 實作形成 RED，證明 helper 確實是為了解決不同 xUnit testhost 的
/// OS process 競態，而不是只測試測試碼本身。helper source 只 link 至 test assembly，
/// 因此本測試可直接呼叫其 internal contract seam，不需要以 reflection 隱藏編譯期型別錯誤。
/// 每個案例使用唯一 temporary lock path；沒有 CRM、Gateway、profile、credential、session 或
/// 使用者資料進入測試，且 finally 一律刪除唯一由本案例建立的空 lock artifact。
/// </summary>
public sealed class WorkerTestHostProcessBoundaryLeaseTests
{
    /// <summary>
    /// 保護跨 testhost 的排他契約：第一個 owner 持有 WorkerTestHost 程序邊界時，第二個 owner
    /// 必須在有界期限後 fail closed，不能繼續執行而把別人的 worker 誤判為自身洩漏。
    /// </summary>
    [Fact]
    public void Concurrent_owner_is_rejected_after_the_bounded_process_boundary_deadline()
    {
        var path = CreateUniqueLockPath();

        try
        {
            using var firstOwner = AcquireForTesting(
                path,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10));

            var action = () => AcquireForTesting(
                path,
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(5));

            action.Should().Throw<TimeoutException>()
                .WithMessage("The WorkerTestHost process-boundary test lease could not be acquired before the bounded deadline.");
        }
        finally
        {
            DeleteTestOwnedLockArtifact(path);
        }
    }

    /// <summary>
    /// 保護 deterministic cleanup 契約：owner dispose 後，下一個 testhost 必須可取得同一路徑；
    /// 這防止 shared fixture 留下 static handle、未釋放 FileStream 或永久 poisoned test session。
    /// </summary>
    [Fact]
    public void Disposed_owner_releases_the_process_boundary_for_the_next_testhost()
    {
        var path = CreateUniqueLockPath();

        try
        {
            var firstOwner = AcquireForTesting(
                path,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10));
            firstOwner.Dispose();

            using var secondOwner = AcquireForTesting(
                path,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10));
            secondOwner.Should().NotBeNull();
        }
        finally
        {
            DeleteTestOwnedLockArtifact(path);
        }
    }

    /// <summary>
    /// 保護 fail-closed 錯誤分類：只有另一個 owner 的 sharing violation 可以等待；不存在的目錄、
    /// 權限或磁碟錯誤必須立即保留原始例外，避免測試基礎設施把真實 resource defect 偽裝成 contention timeout。
    /// </summary>
    [Fact]
    public void Non_contention_file_failure_is_not_retried_or_reclassified_as_a_timeout()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "speechmessage-worker-testhost-missing-" + Guid.NewGuid().ToString("N"),
            "lease.lock");

        var action = () => AcquireForTesting(
            path,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(5));

        action.Should().Throw<DirectoryNotFoundException>();
    }

    /// <summary>
    /// 保護 worktree 隔離：相同 solution root 的兩個 testhost 必須取得相同 lock path；不同 checkout
    /// 則不得互相等待，否則無關分支會產生假性 timeout，降低平行測試吞吐量卻沒有增加安全性。
    /// </summary>
    [Fact]
    public void Lock_path_is_stable_within_one_worktree_and_partitioned_between_worktrees()
    {
        var temporaryDirectory = Path.GetTempPath();
        var sameRootFirst = BuildLockPathForTesting(temporaryDirectory, @"D:\build\worktree-a");
        var sameRootSecond = BuildLockPathForTesting(temporaryDirectory, @"D:\build\worktree-a");
        var differentRoot = BuildLockPathForTesting(temporaryDirectory, @"D:\build\worktree-b");

        sameRootFirst.Should().Be(sameRootSecond);
        differentRoot.Should().NotBe(sameRootFirst);
        Path.GetDirectoryName(sameRootFirst).Should().Be(temporaryDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
    }

    /// <summary>
    /// 以 shared helper 的 internal test seam 取得真正的 interprocess FileStream owner；不 mock
    /// 檔案系統或 OS lock。此 seam 僅存在於兩個 link source 的 test assembly，絕不進入產品或
    /// Gateway production assembly，也不能承載 deployment 設定或呼叫端 authority。
    /// </summary>
    private static IDisposable AcquireForTesting(
        string path,
        TimeSpan timeout,
        TimeSpan retryInterval)
    {
        return WorkerTestHostProcessBoundaryLease.AcquireForTesting(path, timeout, retryInterval);
    }

    /// <summary>
    /// 呼叫 shared helper 的純 path derivation seam，驗證 hash partition 不需建立檔案、控制碼、worker 或
    /// 背景工作；因此它不能保留跨測試狀態或洩漏任何 deployment／user data。
    /// </summary>
    private static string BuildLockPathForTesting(string temporaryDirectory, string solutionRoot)
    {
        return WorkerTestHostProcessBoundaryLease.BuildLockPathForTesting(temporaryDirectory, solutionRoot);
    }

    /// <summary>
    /// 建立本案例唯一的 local temporary path；名稱不含 profile、process ID、endpoint、credential 或
    /// 使用者資料，避免測試程序之間以可識別資料建立 shared state。
    /// </summary>
    private static string CreateUniqueLockPath() => Path.Combine(
        Path.GetTempPath(),
        "speechmessage-worker-testhost-lease-contract-" + Guid.NewGuid().ToString("N") + ".lock");

    /// <summary>
    /// 僅清除本測試用唯一隨機檔名；若 owner 尚未釋放，刪除失敗必須讓測試暴露 cleanup defect，
    /// 不能吞掉例外或碰觸任何共用／產品檔案。
    /// </summary>
    private static void DeleteTestOwnedLockArtifact(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
