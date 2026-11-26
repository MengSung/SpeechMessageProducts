/*
檔案整體說明
目的與定位：保護 FS 電腦級組織寫入鎖的部分取得失敗清理契約，避免一個命令失敗後，
同工作樹的後續命令仍被未釋放的本地檔案控制代碼阻擋。這是離線檔案系統驗證，沒有 D365。
主要流程：每案建立獨立臨時組織目錄與共享鎖目錄，以同名目錄占用共享鎖的檔案位置，
讓 FileStream 在已取得本地鎖後拋出非 IOException 的存取例外；隨後立即獨占開啟本地鎖，
證明清理不依賴 GC、終結器、程序結束或再次重試。所有資料皆為合成路徑與固定組織別名。
資源與隔離：測試唯一擁有隨機臨時根目錄及驗證串流，finally 只清除此根目錄；
不更改 ACL、不觸及共用 ProgramData 鎖、不保存使用者、租戶、憑證或跨測試可變狀態。
故障與決定性斷言：原 UnauthorizedAccessException 必須保留，且本地鎖立即可被 FileShare.None
取得；任一條不成立即失敗。此案例不能代替真正多程序部署互斥或實機驗收結果。
*/
using System;
using System.IO;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>以本案專屬檔案系統故障證明部分取得租約不殘留，不建立真正組織連線。</summary>
public sealed class OrganizationWriteLockLifecycleTests
{
    /// <summary>
    /// 共享鎖位置是目錄時保留非 I/O 例外，並在回傳失敗前釋放本地鎖；
    /// 使用立即獨占開啟作決定性斷言，不透過 GC 或等待掩蓋資源遺留。
    /// </summary>
    [Fact]
    public void Acquire_SharedPathIsDirectory_ReleasesLocalLockBeforeFailureReturns()
    {
        var root = Path.Combine(Path.GetTempPath(), "3p-fs-lock-lifecycle-" + Guid.NewGuid().ToString("N"));
        var organizationRoot = Path.Combine(root, "repository", "construction");
        var sharedRoot = Path.Combine(root, "shared");
        Directory.CreateDirectory(organizationRoot);
        Directory.CreateDirectory(Path.Combine(sharedRoot, "construction.lock"));
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                OrganizationWriteLock.Acquire(organizationRoot, TimeSpan.Zero, sharedRoot));
            using var probe = new FileStream(Path.Combine(organizationRoot, ".deployment-write.lock"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(probe.CanWrite);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
