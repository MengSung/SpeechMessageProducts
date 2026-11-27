/*
檔案整體說明
目的與定位：保護 F3 沙箱安全封套經主機接收後，才標記診斷完成的正式 SDK 契約；離線替身不代表測試組織已部署或 LINE 已送達。
主要功能與流程：以可成功的 Async2 與 RowVersion CAS 模擬同一筆正式申請單，核對真正 ExceptionDiagnostics 落檔、穩定 incident、固定產品及精確投影。注入錯組織、封套竄改、重複身分、競態、通知拒絕及 SDK／清理故障，驗證不會誤確認或讀秘密。
使用邊界與維護：每案唯一 owner 持有暫存日誌、合成 SDK 及時鐘，所有非同步操作 await 後再排空 diagnostics 並刪自身目錄；manager 及 raw SDK 不由產品關閉。測試沒有真實 secrets、個資、網路、計時器或靜態可變資料，並核對 lease 歸還基線。
*/
using System.Globalization;
using System.ServiceModel;
using System.Text.Json;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Tests.Channels;

/// <summary>真正集中式日誌加可故障 SDK 的端到端離線接收契約。</summary>
public sealed class SlotRequestDiagnosticReceiverTests
{
    /// <summary>移除實際 notifier、CAS 或固定 incident 的任一環節都會使本案失敗。</summary>
    [Fact]
    public async Task CommittedMismatch_UsesCommonDiagnosticsThenAcknowledgesExactRow()
    {
        await using var env = new ReceiverEnvironment();
        Assert.Equal(1, await env.Receiver.DrainOnceAsync(default));
        Assert.Equal(2, env.Row.GetAttributeValue<int>("new_f3diagnosticstate"));
        Assert.Equal(new[] { 1, 2 }, env.Updates.Select(u => u.Target.GetAttributeValue<int>("new_f3diagnosticstate")));
        Assert.All(env.Updates, update =>
        {
            Assert.Equal(env.Row.Id, update.Target.Id);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.DoesNotContain("new_f3diagnostic", update.Target.Attributes.Keys);
        });
        using var record = JsonDocument.Parse(Assert.Single(File.ReadAllLines(env.LogPath)));
        Assert.Equal(env.Incident.ToString("N"), record.RootElement.GetProperty("IncidentId").GetString());
        Assert.Equal("SlotRequest.ProductMismatch", record.RootElement.GetProperty("Operation").GetString());
        Assert.Equal("Microsoft.Xrm.Sdk.InvalidPluginExecutionException", record.RootElement.GetProperty("ExceptionType").GetString());
        Assert.Equal("GoodShepherd2", record.RootElement.GetProperty("Product").GetString());
        Assert.Equal(1, env.Returned);
        Assert.Equal(0, env.Faulted);
        var query = Assert.Single(env.Queries);
        Assert.Equal("new_slotrequest", query.EntityName);
        Assert.Equal(16, query.TopCount);
        Assert.False(query.ColumnSet.AllColumns);
        Assert.DoesNotContain(query.ColumnSet.Columns, column => column.Contains("token", StringComparison.OrdinalIgnoreCase) || column.Contains("tenant", StringComparison.Ordinal));
    }

    /// <summary>第二次輪詢已確認列不再通知；去重依資料庫狀態，沒有無界記憶體集合。</summary>
    [Fact]
    public async Task CompletedRow_IsNotReportedAgain()
    {
        await using var env = new ReceiverEnvironment();
        Assert.Equal(1, await env.Receiver.DrainOnceAsync(default));
        Assert.Equal(0, await env.Receiver.DrainOnceAsync(default));
        Assert.Single(File.ReadAllLines(env.LogPath));
        Assert.Equal(2, env.Returned);
    }

    /// <summary>錯組織在任何資料讀取及 CAS 前拒絕，歸還 uncertain lease 並保留原失敗。</summary>
    [Fact]
    public async Task WrongOrganization_FailsBeforeReadOrClaim()
    {
        await using var env = new ReceiverEnvironment { WrongOrganization = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Receiver.DrainOnceAsync(default));
        Assert.Empty(env.Queries); Assert.Empty(env.Updates);
        Assert.Equal(1, env.Returned); Assert.Equal(1, env.Faulted);
    }

    /// <summary>改產品、型別、重複欄位或未知敏感欄位都必須整批拒絕，不能把封套當可信指令。</summary>
    [Theory]
    [InlineData("product", "GoodButler")]
    [InlineData("exceptionType", "System.Exception")]
    [InlineData("extra", "synthetic-secret")]
    [InlineData("duplicate", "synthetic-secret")]
    public async Task InvalidEnvelope_FailsBeforeClaim(string field, string value)
    {
        await using var env = new ReceiverEnvironment();
        var text = env.Row.GetAttributeValue<string>("new_f3diagnostic");
        if (field == "product") text = text.Replace("GoodShepherd2", value, StringComparison.Ordinal);
        else if (field == "exceptionType") text = text.Replace("Microsoft.Xrm.Sdk.InvalidPluginExecutionException", value, StringComparison.Ordinal);
        else text = text[..^1] + (field == "extra" ? ",\"token\":\"" : ",\"product\":\"") + value + "\"}";
        env.Row["new_f3diagnostic"] = text;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Receiver.DrainOnceAsync(default));
        Assert.Empty(env.Updates);
        Assert.DoesNotContain("synthetic-secret", File.ReadAllText(env.LogPath), StringComparison.Ordinal);
        Assert.Equal(1, env.Returned);
    }

    /// <summary>重複正式 ID 不可靜默去重後繼續送通知，必須在第一個領取前拒絕完整候選。</summary>
    [Fact]
    public async Task DuplicateRowIdentity_FailsBeforeAnyClaim()
    {
        await using var env = new ReceiverEnvironment { Duplicate = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Receiver.DrainOnceAsync(default));
        Assert.Empty(env.Updates); Assert.Equal(1, env.Returned);
    }

    /// <summary>唯一允許忽略的 SDK fault 是正式版本競爭；沒有取得領取權就不能送告警或標記完成。</summary>
    [Fact]
    public async Task ClaimVersionRace_DoesNotNotifyOrAcknowledge()
    {
        await using var env = new ReceiverEnvironment { ClaimConflict = true };
        Assert.Equal(0, await env.Receiver.DrainOnceAsync(default));
        Assert.False(File.Exists(env.LogPath));
        Assert.Equal(0, env.Row.GetAttributeValue<int>("new_f3diagnosticstate"));
        Assert.Equal(1, env.Returned); Assert.Equal(0, env.Faulted);
    }

    /// <summary>落檔入口拒絕事件時不得確認完成，必須釋回 pending，之後可在修復後以相同 incident 重試。</summary>
    [Fact]
    public async Task DiagnosticWriteRejected_RemainsPendingThenCanRecover()
    {
        await using var env = new ReceiverEnvironment();
        Directory.CreateDirectory(env.LogPath);
        Assert.Equal(0, await env.Receiver.DrainOnceAsync(default));
        Assert.Equal(0, env.Row.GetAttributeValue<int>("new_f3diagnosticstate"));
        Directory.Delete(env.LogPath);
        Assert.Equal(1, await env.Receiver.DrainOnceAsync(default));
        Assert.Equal(2, env.Row.GetAttributeValue<int>("new_f3diagnosticstate"));
        Assert.Single(File.ReadAllLines(env.LogPath));
    }

    /// <summary>在 SDK 讀取中正常 caller 取消不告警，但租約仍淘汰、歸還且接收器下一輪可用。</summary>
    [Fact]
    public async Task CallerCancellation_DrainsWithoutAlertAndCanResume()
    {
        await using var env = new ReceiverEnvironment();
        using var cancellation = new CancellationTokenSource();
        env.BeforeQuery = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Receiver.DrainOnceAsync(cancellation.Token));
        Assert.False(File.Exists(env.LogPath));
        Assert.Equal(1, env.Returned); Assert.Equal(1, env.Faulted);
        env.BeforeQuery = null;
        Assert.Equal(1, await env.Receiver.DrainOnceAsync(default));
    }

    /// <summary>SDK 及歸還同時失敗仍傳出兩個原物件，不能以清理錯誤覆蓋真正故障。</summary>
    [Fact]
    public async Task SdkAndCleanupFailures_PreserveBothOriginalExceptions()
    {
        await using var env = new ReceiverEnvironment();
        var original = new IOException("synthetic-secret-do-not-log");
        var cleanup = new InvalidOperationException("synthetic-cleanup");
        env.ReadFault = original; env.CleanupFault = cleanup;
        var result = await Assert.ThrowsAsync<AggregateException>(() => env.Receiver.DrainOnceAsync(default));
        Assert.Equal(new Exception[] { original, cleanup }, result.InnerExceptions);
        Assert.DoesNotContain("synthetic-secret", File.ReadAllText(env.LogPath), StringComparison.Ordinal);
        Assert.Equal(1, env.Faulted); Assert.Equal(1, env.Returned);
    }

    /// <summary>單案合成資料庫及真正日誌 owner；SDK 只接受固定 WhoAmI、診斷投影與正式 RowVersion 更新。</summary>
    private sealed class ReceiverEnvironment : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "f3-diagnostic-receiver-" + Guid.NewGuid().ToString("N"));
        private readonly Guid organization = Guid.NewGuid();
        private readonly ExceptionDiagnostics diagnostics;
        internal Guid Incident { get; } = Guid.NewGuid();
        internal Entity Row { get; }
        internal SlotRequestDiagnosticReceiver Receiver { get; }
        internal string LogPath => Path.Combine(root, "Exception.log");
        internal int Returned, Faulted;
        internal bool WrongOrganization, Duplicate, ClaimConflict;
        internal Exception? ReadFault, CleanupFault;
        internal Action? BeforeQuery;
        internal List<QueryExpression> Queries { get; } = [];
        internal List<UpdateRequest> Updates { get; } = [];

        /// <summary>建立可成功的真正 Async2 mock，更新後增加權威版本，禁止測試繞過 CAS。</summary>
        internal ReceiverEnvironment()
        {
            Directory.CreateDirectory(root);
            diagnostics = new ExceptionDiagnostics(root, outputOptions: new ExceptionOutputOptions(true, false));
            var clock = new TestClock();
            Row = new Entity("new_slotrequest", Guid.NewGuid()) { ["new_result"] = new OptionSetValue(100000002),
                ["new_f3diagnosticstate"] = 0, ["versionnumber"] = 1L };
            Row["new_f3diagnostic"] = JsonSerializer.Serialize(new { version = 1, incident = Incident.ToString("D"),
                utc = clock.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), product = "GoodShepherd2",
                operation = "SlotRequest.ProductMismatch", exceptionType = "Microsoft.Xrm.Sdk.InvalidPluginExecutionException" });
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var asyncSdk = sdk.As<IOrganizationServiceAsync2>();
            asyncSdk.Setup(service => service.ExecuteAsync(It.IsAny<OrganizationRequest>(), It.IsAny<CancellationToken>()))
                .Returns<OrganizationRequest, CancellationToken>((request, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (request is WhoAmIRequest)
                    {
                        var identity = new WhoAmIResponse(); identity.Results["OrganizationId"] = WrongOrganization ? Guid.NewGuid() : organization;
                        return Task.FromResult<OrganizationResponse>(identity);
                    }
                    var update = Assert.IsType<UpdateRequest>(request);
                    if (ClaimConflict || update.Target.RowVersion != Row.GetAttributeValue<long>("versionnumber").ToString(CultureInfo.InvariantCulture))
                        throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147088254 });
                    Updates.Add(update);
                    foreach (var attribute in update.Target.Attributes) Row[attribute.Key] = attribute.Value;
                    Row["versionnumber"] = Row.GetAttributeValue<long>("versionnumber") + 1;
                    return Task.FromResult<OrganizationResponse>(new UpdateResponse());
                });
            asyncSdk.Setup(service => service.RetrieveMultipleAsync(It.IsAny<QueryBase>(), It.IsAny<CancellationToken>()))
                .Returns<QueryBase, CancellationToken>((raw, ct) =>
                {
                    BeforeQuery?.Invoke(); ct.ThrowIfCancellationRequested();
                    var query = Assert.IsType<QueryExpression>(raw); Queries.Add(query);
                    if (ReadFault != null) throw ReadFault;
                    if (Row.GetAttributeValue<int>("new_f3diagnosticstate") == 2) return Task.FromResult(new EntityCollection());
                    var rows = Duplicate ? new[] { Copy(), Copy() } : new[] { Copy() };
                    return Task.FromResult(new EntityCollection(rows.ToList()));
                });
            asyncSdk.Setup(service => service.RetrieveAsync("new_slotrequest", It.IsAny<Guid>(), It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()))
                .Returns<string, Guid, ColumnSet, CancellationToken>((_, id, _, ct) =>
                { ct.ThrowIfCancellationRequested(); Assert.Equal(Row.Id, id); return Task.FromResult(Copy()); });
            Receiver = new SlotRequestDiagnosticReceiver("GoodShepherd2", organization, ct =>
            {
                ct.ThrowIfCancellationRequested();
                var lease = new Mock<IClientLease>(MockBehavior.Strict);
                lease.SetupGet(value => value.Service).Returns(sdk.Object);
                lease.Setup(value => value.MarkFaulted()).Callback(() => Faulted++);
                lease.Setup(value => value.Dispose()).Callback(() => { Returned++; if (CleanupFault != null) throw CleanupFault; });
                return lease.Object;
            }, clock, diagnostics);
        }

        /// <summary>回傳 detached 正式列，讓產品不能修改 mock 的權威資料來假裝 SDK 已提交。</summary>
        private Entity Copy()
        {
            var copy = new Entity(Row.LogicalName, Row.Id);
            foreach (var value in Row.Attributes) copy[value.Key] = value.Value;
            return copy;
        }

        /// <summary>操作均已 await；先 drain 真診斷 owner，再刪唯一測試目錄，不碰共用路徑。</summary>
        public async ValueTask DisposeAsync()
        {
            await diagnostics.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
