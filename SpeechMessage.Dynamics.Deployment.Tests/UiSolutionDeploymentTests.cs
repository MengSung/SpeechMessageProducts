/*
檔案整體說明
目的與定位：FS 安全補正的離線匯入資源測試，直接呼叫 UiSolutionDeployment.Import，驗證磁碟輸入在進入 SDK 前即受預算及取消約束。
主要功能與流程：以單案例暫存 ZIP 注入 16MiB 加一及 64MiB 超額檔案、可壓縮的累計解壓超額內容與預先取消；斷言固定格式拒絕或呼叫端取消，並量測正式匯入的執行緒配置量，防止完整 ReadAllBytes 後才拒絕。
信任邊界與擁有權：輸入全為合成資料，SDK 替身拒絕意外呼叫，不連組織、不讀秘密、不建立背景工作。案例以 finally 刪除自己唯一命名的檔案；每次失敗後立即以 FileShare.None 重開，證明匯入的檔案 owner 已同步關閉。
驗證限制：這些決定性斷言只證明離線預算、取消及檔案句柄釋放，不代表實機匯入、發布或長期記憶體量測。
*/
using System;
using System.IO;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護匯入讀取前的資源邊界；案例專屬檔案與取消來源均在斷言失敗時清理。</summary>
public sealed class UiSolutionDeploymentTests
{
    /// <summary>
    /// 超額檔案重試三次皆須在大型配置及任何 SDK 呼叫前拒絕；2MiB 容差小於最小超額檔案。
    /// 每輪獨占重開原檔作為句柄排空斷言，避免只證明最後會擲例外。
    /// </summary>
    [Theory]
    [InlineData(16 * 1024 * 1024 + 1)]
    [InlineData(64 * 1024 * 1024)]
    public void Import_OversizedFile_RejectedBeforeLargeAllocation_AndReleasesHandle(int fileBytes)
    {
        var path = TemporaryArchivePath();
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.SetLength(fileBytes);
            var service = new RejectingService();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var error = Record.Exception(() => UiSolutionDeployment.Import(
                    service, path, "SpeechMessageUi", CancellationToken.None));
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.IsType<InvalidDataException>(error);
                Assert.InRange(allocated, 0, 2 * 1024 * 1024);
                using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.Equal(fileBytes, reopened.Length);
            }
            Assert.Equal(0, service.Calls);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>壓縮後很小但累計解壓 18MiB 的 ZIP 必須在 SDK 前拒絕，並於失敗後同步釋放輸入句柄。</summary>
    [Fact]
    public void Import_ExpandedTotalOverBudget_RefusedBeforeSdk_AndReleasesHandle()
    {
        var path = TemporaryArchivePath();
        try
        {
            File.WriteAllBytes(path, UiSolutionArchiveTests.ArchiveAtExpandedSize(
                18 * 1024 * 1024, 6 * 1024 * 1024));
            var service = new RejectingService();

            Assert.Throws<InvalidDataException>(() => UiSolutionDeployment.Import(
                service, path, "SpeechMessageUi", CancellationToken.None));

            Assert.Equal(0, service.Calls);
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(reopened.Length < 1024 * 1024);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>呼叫端已取消時不先讀取超額檔案；保留同一 cancellation token，且不留下檔案 owner 或 SDK 呼叫。</summary>
    [Fact]
    public void Import_PreCanceled_DoesNotReadOrCallSdk()
    {
        var path = TemporaryArchivePath();
        using var cancellation = new CancellationTokenSource();
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.SetLength(64 * 1024 * 1024);
            var service = new RejectingService();
            cancellation.Cancel();

            var error = Assert.Throws<OperationCanceledException>(() => UiSolutionDeployment.Import(
                service, path, "SpeechMessageUi", cancellation.Token));

            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(0, service.Calls);
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(64 * 1024 * 1024, reopened.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>只產生本案例的唯一暫存檔路徑；建立與 finally 刪除責任留在呼叫案例。</summary>
    private static string TemporaryArchivePath() =>
        Path.Combine(Path.GetTempPath(), "smv-ui-budget-" + Guid.NewGuid().ToString("N") + ".zip");

    /// <summary>沒有連線或 disposable；每案只記錄 SDK 越界次數，任何意外 SDK 呼叫立即失敗。</summary>
    private sealed class RejectingService : IOrganizationService
    {
        /// <summary>目前案例的 SDK 進入次數，不保存任何組織或請求內容。</summary>
        internal int Calls { get; private set; }

        /// <summary>預算拒絕不應抵達 SDK；若抵達，保留次數並用固定例外使測試失敗。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Calls++;
            throw new InvalidOperationException("Archive validation must finish before SDK access.");
        }

        /// <summary>讀取拒絕案例禁止建立資料列。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>讀取拒絕案例禁止更新資料列。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>讀取拒絕案例禁止刪除資料列。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>讀取拒絕案例禁止讀取資料列。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>讀取拒絕案例禁止查詢。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>讀取拒絕案例禁止建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
        /// <summary>讀取拒絕案例禁止解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
    }
}
