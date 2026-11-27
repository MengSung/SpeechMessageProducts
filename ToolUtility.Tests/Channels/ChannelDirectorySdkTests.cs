/*
檔案整體說明
目的與定位：以真正 QueryExpression、UpdateRequest 與 Async2 SDK 介面的離線替身驗證 F3 adapter，不讀 secrets、不建立實際 CRM 連線。
主要功能與流程：合成 SDK 有可成功分頁、讀取密文及健康 CAS 的合法路徑；另有實際套用產品條件與欄位投影的路徑，驗證完整刷新僅以一筆產品欄位偵測外產品、同一來源只告警一次且本產品仍成功。注入錯組織、空產品、他產品、重複正式 ID、換版及清理故障，斷言秘密精確條件、原例外與 lease 歸還次序。
使用邊界與維護：測試 owner 管理 mock、有限查詢紀錄及合成資料，adapter 只歸還租約，不可 Dispose manager 或 raw service。每次方法應只有一個 lease；閘門在 finally 開放且所有 async 呼叫在結束前 await，另以保留已釋放 lease 的弱參照驗證 SDK 與來源可回收。沒有真實設定、秘密或背景 Task；離線替身不能宣稱已驗證 D365 欄位權限或實機樂觀鎖支援。
*/
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Dataverse;
using Xunit;
namespace ToolUtility.Tests.Channels;

/// <summary>保護產品過濾、正式唯一主鍵、可取消 SDK 及租約 owner 契約。</summary>
public sealed class ChannelDirectorySdkTests
{
    /// <summary>
    /// 保護資料庫精確過濾後仍能發現外產品的契約：替身確實執行條件與投影，外產品的秘密使用不可轉型物件。
    /// 先讀純 metadata 不告警，再兩次完整刷新；本產品憑證必須成功、偵測最多一筆且僅投影產品、同來源只上報一次。
    /// </summary>
    [Fact]
    public async Task Sdk_FullRefresh_FilteredForeignProduct_AlertsOnceWithoutReadingSecrets()
    {
        var env = new SdkEnvironment { ApplyServerConditions = true };
        var own = env.Row("10001");
        var foreign = env.Row("20001"); foreign["new_product"] = "GoodButler";
        foreign["new_accesstokenencrypted"] = new object(); foreign["new_channelsecretencrypted"] = new object();
        env.Rows = [own, foreign];
        Assert.Single(await ReadAsync(env.Source, false));
        Assert.Empty(env.Notifications);
        Assert.DoesNotContain(env.Queries, IsForeignProbe);

        for (var refresh = 0; refresh < 2; refresh++)
        {
            var result = Assert.Single(await ReadAsync(env.Source, true));
            Assert.Equal(own.Id, result.Id);
            Assert.Equal("synthetic-envelope", result.TokenEncrypted);
        }

        var probes = env.Queries.Where(IsForeignProbe).ToArray();
        Assert.Equal(2, probes.Length);
        Assert.All(probes, query =>
        {
            Assert.Equal(1, query.TopCount);
            Assert.False(query.ColumnSet.AllColumns);
            Assert.Equal(new[] { "new_product" }, query.ColumnSet.Columns);
            Assert.Equal(LogicalOperator.And, query.Criteria.FilterOperator);
            Assert.Equal(3, query.Criteria.Conditions.Count);
            Assert.Contains(query.Criteria.Conditions, condition => condition.AttributeName == "new_product" && condition.Operator == ConditionOperator.NotNull);
            Assert.Contains(query.Criteria.Conditions, condition => condition.AttributeName == "new_product" && condition.Operator == ConditionOperator.NotEqual && Equals(condition.Values.Single(), "GoodShepherd2"));
            Assert.Contains(query.Criteria.Conditions, condition => condition.AttributeName == "new_product" && condition.Operator == ConditionOperator.NotEqual && Equals(condition.Values.Single(), ""));
        });
        var notification = Assert.Single(env.Notifications);
        Assert.IsType<InvalidOperationException>(notification.Failure);
        Assert.Equal("ChannelDirectory.ForeignProduct", notification.Operation);
        Assert.Null(notification.Failure.InnerException);
        Assert.Empty(notification.Failure.Data);
        Assert.DoesNotContain("GoodButler", notification.Failure.Message);
        Assert.All(env.Queries.Where(query => !IsForeignProbe(query)), query =>
            Assert.Contains(query.Criteria.Conditions, condition => condition.AttributeName == "new_product" && condition.Operator == ConditionOperator.Equal && Equals(condition.Values.Single(), "GoodShepherd2")));
        Assert.All(env.Queries.Where(query => query.ColumnSet.Columns.Contains("new_accesstokenencrypted")), query =>
            Assert.Equal(own.Id, query.Criteria.Conditions.Single(condition => condition.AttributeName == "new_linechannelid").Values.Single()));
        Assert.Equal(3, env.WhoAmICalls);
        Assert.Equal(3, env.Acquired);
        Assert.Equal(3, env.Returned);
        Assert.Equal(0, env.Faulted);
    }

    /// <summary>
    /// 空白產品屬於複製舊資料，不能告警或取秘密；以 null、空字串及空白分別模擬資料庫回應。
    /// 本產品在同一次刷新仍取得合法憑證，所有租約歸還且不淘汰健康連線。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task Sdk_FullRefresh_BlankProduct_DoesNotAlertOrReadForeignSecrets(string? product)
    {
        var env = new SdkEnvironment { ApplyServerConditions = true };
        var blank = env.Row("20001"); blank["new_product"] = product;
        blank["new_accesstokenencrypted"] = new object(); blank["new_channelsecretencrypted"] = new object();
        env.Rows = [env.Row("10001"), blank];
        var result = Assert.Single(await ReadAsync(env.Source, true));
        Assert.Equal("10001", result.ChannelKey);
        Assert.Equal("synthetic-envelope", result.TokenEncrypted);
        Assert.Single(env.Queries.Where(IsForeignProbe));
        Assert.Empty(env.Notifications);
        Assert.Equal(1, env.Acquired);
        Assert.Equal(1, env.Returned);
        Assert.Equal(0, env.Faulted);
    }

    /// <summary>
    /// 偵測查詢內正常取消不得產生外產品告警或繼續讀秘密；以 SDK 入場 callback 取消原 caller token。
    /// 取消必須傳回原呼叫者，uncertain lease 淘汰後歸還，沒有新的 WhoAmI 或背景工作。
    /// </summary>
    [Fact]
    public async Task Sdk_ForeignProbe_CallerCancellation_DrainsWithoutAlertOrSecretRead()
    {
        var env = new SdkEnvironment { ApplyServerConditions = true };
        env.Rows = [env.Row("10001")];
        using var cancellation = new CancellationTokenSource();
        env.BeforeQuery = query => { if (IsForeignProbe(query)) cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAsync(env.Source, true, cancellation.Token));
        Assert.Empty(env.Notifications);
        Assert.DoesNotContain(env.Queries, query => query.ColumnSet.Columns.Contains("new_accesstokenencrypted"));
        Assert.Equal(1, env.Faulted);
        Assert.Equal(1, env.Returned);
    }

    /// <summary>
    /// 偵測 SDK 故障不可靜默當成沒有外產品；注入同一原始例外及租約歸還故障。
    /// 兩原物件必須聚合、秘密零查詢，只有一次淘汰及歸還；最終故障仍由上層目錄上報。
    /// </summary>
    [Fact]
    public async Task Sdk_ForeignProbe_FaultAndCleanup_PreservesBothAndDrains()
    {
        var env = new SdkEnvironment { ApplyServerConditions = true };
        env.Rows = [env.Row("10001")];
        var failure = new IOException("Synthetic probe failure.");
        var cleanup = new InvalidOperationException("Synthetic lease cleanup failure.");
        env.ProbeFault = failure; env.CleanupFault = cleanup;
        var actual = await Assert.ThrowsAsync<AggregateException>(() => ReadAsync(env.Source, true));
        Assert.Equal(new Exception[] { failure, cleanup }, actual.InnerExceptions);
        Assert.Empty(env.Notifications);
        Assert.DoesNotContain(env.Queries, query => query.ColumnSet.Columns.Contains("new_accesstokenencrypted"));
        Assert.Equal(1, env.Faulted);
        Assert.Equal(1, env.Returned);
    }

    /// <summary>
    /// Dispose 必須等待已入場的 SDK 偵測排空，且先拒絕新操作，不得將仍在使用的租約交還連線池。
    /// 合成閘門停在偵測 I/O；finally 必開閘並 await 讀取及關閉，歸還一次後重複 Dispose 不再碰 SDK。
    /// </summary>
    [Fact]
    public async Task Sdk_SourceLease_DisposeDuringProbe_WaitsForDrainAndRejectsNewReads()
    {
        var env = new SdkEnvironment { ApplyServerConditions = true };
        env.Rows = [env.Row("10001")];
        env.ProbeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        env.ProbeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = await env.Source.OpenAsync(default);
        var reading = lease.ReadAsync(true, default);
        Task closing = Task.CompletedTask;
        try
        {
            await env.ProbeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closing = lease.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Equal(0, env.Returned);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => lease.ReadAsync(false, default));
        }
        finally
        {
            env.ProbeRelease.TrySetResult();
            await reading.WaitAsync(TimeSpan.FromSeconds(5));
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            await lease.DisposeAsync();
        }
        Assert.Equal(1, env.Returned);
        Assert.Equal(0, env.Faulted);
    }

    /// <summary>
    /// 呼叫端可保留已關閉 lease，卻不能因此延長來源工廠與 raw SDK 的生命週期。
    /// 使用已完成的真 adapter 操作及弱參照強制 GC，決定性驗證兩依賴可回收且後續讀寫被拒絕。
    /// </summary>
    [Fact]
    public async Task Sdk_DisposedLease_DoesNotRetainSourceOrSdkAndRejectsOperations()
    {
        var retained = CreateDisposedLease();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(retained.Source.IsAlive);
        Assert.False(retained.Service.IsAlive);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => retained.Lease.ReadAsync(false, default));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => retained.Lease.WriteHealthAsync(null!, null!, false, default));
        GC.KeepAlive(retained.Lease);
    }

    /// <summary>限制 GC 測試的區域根生命週期；helper 結束時只交出已 Dispose lease 及來源／SDK 弱參照。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IChannelDirectorySourceLease Lease, WeakReference Source, WeakReference Service) CreateDisposedLease()
    {
        var env = new SdkEnvironment();
        env.Rows = [env.Row("10001")];
        var lease = env.Source.OpenAsync(default).GetAwaiter().GetResult();
        lease.ReadAsync(false, default).GetAwaiter().GetResult();
        lease.DisposeAsync().GetAwaiter().GetResult();
        return (lease, new WeakReference(env.Source), new WeakReference(env.Service));
    }

    /// <summary>辨識只投影產品的有界偵測，避免把偵測混成既有逐正式 ID 的秘密查詢。</summary>
    private static bool IsForeignProbe(QueryExpression query) => query.TopCount == 1
        && query.ColumnSet.Columns.SequenceEqual(new[] { "new_product" });

    /// <summary>
    /// 同一來源的三十二個租約同時停在偵測 I/O，開閘後仍只能由 owner 發出一次告警。
    /// 所有讀取必須返回本產品憑證；finally 開閘並等待全部操作，租約與 WhoAmI 數回到三十二次且零淘汰。
    /// </summary>
    [Fact]
    public async Task Sdk_ConcurrentFullRefresh_AlertsOncePerSourceAndReturnsEveryLease()
    {
        var env = new SdkEnvironment { ApplyServerConditions = true };
        var foreign = env.Row("20001"); foreign["new_product"] = "GoodButler";
        env.Rows = [env.Row("10001"), foreign];
        env.ProbeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = Enumerable.Range(0, 32).Select(_ => ReadAsync(env.Source, true)).ToArray();
        try
        {
            Assert.Equal(32, env.Queries.Count(IsForeignProbe));
            Assert.Equal(0, env.Returned);
        }
        finally
        {
            env.ProbeRelease.TrySetResult();
            await Task.WhenAll(reads).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.All(reads, read => Assert.Equal("10001", Assert.Single(read.Result).ChannelKey));
        Assert.Single(env.Notifications);
        Assert.Equal(32, env.Acquired);
        Assert.Equal(32, env.WhoAmICalls);
        Assert.Equal(32, env.Returned);
        Assert.Equal(0, env.Faulted);
    }

    /// <summary>
    /// 診斷管道故障不能覆蓋本產品憑證或引發遞迴通知；以第一次 Report 拋出合成故障。
    /// 再次完整刷新仍成功、Report 只進入一次、每個 SDK lease 正常歸還；固定 stderr 不保存原錯誤本文。
    /// </summary>
    [Fact]
    public async Task Sdk_ForeignAlert_DiagnosticsFault_DoesNotRecurseOrFailOwnCredentials()
    {
        var env = new SdkEnvironment { ApplyServerConditions = true, DiagnosticsFault = new IOException("Synthetic diagnostics fault.") };
        var foreign = env.Row("20001"); foreign["new_product"] = "GoodButler";
        env.Rows = [env.Row("10001"), foreign];
        for (var refresh = 0; refresh < 2; refresh++)
            Assert.Equal("synthetic-envelope", Assert.Single(await ReadAsync(env.Source, true)).TokenEncrypted);
        Assert.Single(env.Notifications);
        Assert.Equal(2, env.Returned);
        Assert.Equal(0, env.Faulted);
    }

    /// <summary>
    /// SDK 內部取消不等於 caller 正常取消；以已取消的內部 token 注入原始 OCE，caller 維持未取消。
    /// 真目錄必須上報同一原物件一次、拒絕發布憑證並淘汰／歸還租約；目錄及合成金鑰由測試 owner 確定關閉。
    /// </summary>
    [Fact]
    public async Task Sdk_ForeignProbe_InternalCancellation_IsReportedByDirectoryAndDrained()
    {
        using var fixture = new DirectoryFixture();
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();
        var failure = new OperationCanceledException("Synthetic SDK internal timeout.", timeout.Token);
        var env = new SdkEnvironment { ApplyServerConditions = true, ProbeFault = failure };
        env.Rows = [env.Row("10001")];
        await using var directory = new DataverseChannelDirectory("GoodShepherd2", env.Source.OrganizationId, env.Source,
            fixture.Key, fixture.Clock, env.Diagnostics, decrypt: static (_, _) => "synthetic");
        var actual = Assert.ThrowsAny<OperationCanceledException>(() => directory.TryGetCredential("10001", out _));
        Assert.Same(failure, actual);
        var notification = Assert.Single(env.Notifications);
        Assert.Same(failure, notification.Failure);
        Assert.Equal("ChannelDirectory.ResolveCredential", notification.Operation);
        Assert.Equal(1, env.Faulted);
        Assert.Equal(1, env.Returned);
        Assert.DoesNotContain(env.Queries, query => query.ColumnSet.Columns.Contains("new_accesstokenencrypted"));
    }

    /// <summary>真 SDK 查詢先 filter 產品，精確欄位、分頁、合法同名不同 ID 皆保留，且只借一次。</summary>
    [Fact]
    public async Task Sdk_PagedMetadataThenSecrets_ExactColumnsAndOneLease()
    {
        var env = new SdkEnvironment();
        var first = env.Row("10001");
        var second = env.Row("10002");
        env.Rows = [first, second];
        env.PageSize = 1;
        var result = await ReadAsync(env.Source, true);
        Assert.Equal(2, result.Count);
        Assert.All(result, row => Assert.Equal("synthetic-envelope", row.TokenEncrypted));
        Assert.Equal(1, env.Acquired);
        Assert.Equal(1, env.Returned);
        Assert.Equal(0, env.Faulted);
        var metadataQueries = env.Queries.Where(query => query.TopCount is null).ToArray();
        var secretQueries = env.Queries.Where(query => query.TopCount is not null && !IsForeignProbe(query)).ToArray();
        Assert.Single(env.Queries.Where(IsForeignProbe));
        Assert.Equal(2, metadataQueries.Length);
        Assert.Equal(2, secretQueries.Length);
        Assert.All(metadataQueries, query =>
        {
            Assert.False(query.ColumnSet.AllColumns);
            Assert.Equal(new[] { "new_linechannelid", "new_product", "new_channelid", "new_channelstatus", "modifiedon", "versionnumber" }, query.ColumnSet.Columns);
        });
        Assert.All(secretQueries, query =>
            Assert.Equal(new[] { "new_product", "new_accesstokenencrypted", "new_channelsecretencrypted" }, query.ColumnSet.Columns));
        Assert.All(env.Queries, query => Assert.DoesNotContain("new_name", query.ColumnSet.Columns));
        Assert.All(env.Queries.Where(query => !IsForeignProbe(query)), query =>
            Assert.Contains(query.Criteria.Conditions, c => c.AttributeName == "new_product" && c.Operator == ConditionOperator.Equal && Equals(c.Values.Single(), "GoodShepherd2")));
        Assert.Equal(
            result.Select(row => row.Id).OrderBy(id => id),
            secretQueries.Select(query => (Guid)query.Criteria.Conditions.Single(c => c.AttributeName == "new_linechannelid").Values.Single()).OrderBy(id => id));
        Assert.Equal(2, result.Select(row => row.Id).Distinct().Count());
    }

    /// <summary>伺服器忽略 filter 的惡意回應仍逐列重驗，外產品壞型別密文不應被讀到。</summary>
    [Fact]
    public async Task Sdk_ForeignAndBlankProduct_NeverReadCiphertext()
    {
        var env = new SdkEnvironment();
        var foreign = env.Row("20001"); foreign["new_product"] = "GoodButler";
        foreign["new_accesstokenencrypted"] = new object(); foreign["new_channelsecretencrypted"] = new object();
        var blank = env.Row("30001"); blank.Attributes.Remove("new_product");
        blank["new_accesstokenencrypted"] = new object();
        env.Rows = [env.Row("10001"), foreign, blank];
        var result = await ReadAsync(env.Source, true);
        Assert.Single(result.Where(row => row.Product == "GoodShepherd2"));
        Assert.All(result.Where(row => row.Product != "GoodShepherd2"), row => Assert.Null(row.TokenEncrypted));
        Assert.Equal(0, env.Faulted);
    }

    /// <summary>重複正式 Id 在密文查詢前拒絕，不以名稱或 Distinct 修補。</summary>
    [Fact]
    public async Task Sdk_DuplicateOfficialId_RejectsBeforeSecretQuery()
    {
        var env = new SdkEnvironment();
        var first = env.Row("10001");
        var second = env.Row("10002"); second.Id = first.Id; second["new_linechannelid"] = first.Id;
        env.Rows = [first, second];
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(env.Source, true));
        Assert.Single(env.Queries);
        Assert.Equal(1, env.Returned);
        Assert.Equal(1, env.Faulted);
    }

    /// <summary>實際 SDK 組織不符在任何資料查詢前拒絕且淘汰租約，不將 caller 組織當權威。</summary>
    [Fact]
    public async Task Sdk_WrongOrganization_RefusedBeforeQuery()
    {
        var env = new SdkEnvironment { ServerOrganization = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(env.Source, true));
        Assert.Empty(env.Queries);
        Assert.Equal(1, env.Faulted);
        Assert.Equal(1, env.Returned);
    }

    /// <summary>健康回寫只用重驗的正式 Id／產品／RowVersion，啟用 CAS 且不包含 token 或產品修改。</summary>
    [Fact]
    public async Task Sdk_HealthWrite_RevalidatesOfficialTargetAndUsesCas()
    {
        var env = new SdkEnvironment();
        var row = env.Row("10001"); env.Rows = [row];
        var record = Assert.Single(await ReadAsync(env.Source, false));
        await WriteHealthAsync(env.Source, record, new ChannelHealthResult(true, 17, 3, new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero)), false);
        var update = Assert.Single(env.Updates);
        Assert.Equal(row.Id, update.Target.Id);
        Assert.Equal("1", update.Target.RowVersion);
        Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
        Assert.Equal(3, update.Target.Attributes.Count);
        Assert.Equal(17, update.Target["new_monthlyusage"]);
        Assert.Equal(3, update.Target["new_actualrichmenucount"]);
        Assert.Equal(new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc), update.Target["new_lastcheckedon"]);
        Assert.DoesNotContain("new_product", update.Target.Attributes.Keys);
        Assert.Equal(2, env.Acquired);
        Assert.Equal(2, env.Returned);
        Assert.DoesNotContain(env.Queries, IsForeignProbe);
    }

    /// <summary>
    /// 保護健康檢查收到 401 時，正式 SDK adapter 只停用已重驗的 Channel，並以 RowVersion CAS 寫回檢查時間。
    /// 離線 SDK 替身回傳一筆同 ID／產品／版本的合法目標；斷言狀態 choice、精確欄位集合及租約歸還。
    /// </summary>
    [Fact]
    public async Task Sdk_HealthUnauthorized_DisablesExactChannelWithCas()
    {
        var env = new SdkEnvironment();
        var row = env.Row("10001"); env.Rows = [row];
        var record = Assert.Single(await ReadAsync(env.Source, false));
        var checkedAt = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        await WriteHealthAsync(env.Source, record, new ChannelHealthResult(false, 0, 0, checkedAt), true);
        var update = Assert.Single(env.Updates);
        Assert.Equal(row.Id, update.Target.Id);
        Assert.Equal("1", update.Target.RowVersion);
        Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
        Assert.Equal(2, update.Target.Attributes.Count);
        Assert.Equal(100000003, Assert.IsType<OptionSetValue>(update.Target["new_channelstatus"]).Value);
        Assert.Equal(checkedAt.UtcDateTime, update.Target["new_lastcheckedon"]);
        Assert.DoesNotContain("new_accesstokenencrypted", update.Target.Attributes.Keys);
        Assert.Equal(2, env.Acquired);
        Assert.Equal(2, env.Returned);
        Assert.Equal(0, env.Faulted);
    }

    /// <summary>健康期間憑證旋轉後，舊操作不得停用新版本。</summary>
    [Fact]
    public async Task Sdk_HealthWrite_RotatedRevisionCannotDisableNewCredential()
    {
        var env = new SdkEnvironment();
        var row = env.Row("10001"); env.Rows = [row];
        var record = Assert.Single(await ReadAsync(env.Source, false));
        row["versionnumber"] = 2L;
        await Assert.ThrowsAsync<InvalidOperationException>(() => WriteHealthAsync(env.Source, record,
            new ChannelHealthResult(false, 0, 0, new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero)), true));
        Assert.Empty(env.Updates);
        Assert.Equal(2, env.Returned);
    }

    /// <summary>
    /// Dataverse SDK 在租約已借出後取消時必須淘汰連線再歸還，避免 uncertain client 交給下一請求。
    /// 合成 Async2 在第一次 RetrieveMultiple 內取消 caller token；斷言原取消傳出、lease faulted 一次且歸還一次。
    /// </summary>
    [Fact]
    public async Task Sdk_CancellationDuringRead_FaultsAndReturnsLease()
    {
        var env = new SdkEnvironment();
        env.Rows = [env.Row("10001")];
        using var cancellation = new CancellationTokenSource();
        env.BeforeRead = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAsync(env.Source, false, cancellation.Token));
        Assert.Equal(1, env.Faulted);
        Assert.Equal(1, env.Returned);
        Assert.Empty(env.Updates);
    }

    /// <summary>不前進的分頁與超量資料有界拒絕；租約歸還，不能留下無限迴圈。</summary>
    [Fact]
    public async Task Sdk_ExcessivePaging_FailsClosedAndReturnsLease()
    {
        var env = new SdkEnvironment { RepeatPage = true };
        env.Rows = [env.Row("10001")];
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(env.Source, false));
        Assert.InRange(env.Queries.Count, 1, 17);
        Assert.Equal(1, env.Returned);
    }

    /// <summary>SDK 與租約清理皆故障時保留兩個原例外，先標記淘汰，再嘗試歸還。</summary>
    [Fact]
    public async Task Sdk_FaultAndCleanup_PreservesOriginalExceptions()
    {
        var env = new SdkEnvironment();
        var fault = new InvalidOperationException("Synthetic SDK fault.");
        var cleanup = new IOException("Synthetic lease failure.");
        env.ReadFault = fault; env.CleanupFault = cleanup;
        var actual = await Assert.ThrowsAsync<AggregateException>(() => ReadAsync(env.Source, false));
        Assert.Equal(new Exception[] { fault, cleanup }, actual.InnerExceptions);
        Assert.Equal(1, env.Faulted);
        Assert.Equal(1, env.Returned);
    }

    /// <summary>每個測試操作只借用一個來源 lease，await 完整讀取後由測試 helper 歸還。</summary>
    private static async Task<IReadOnlyList<ChannelDirectoryRecord>> ReadAsync(IChannelDirectorySource source, bool credentials, CancellationToken cancellationToken = default)
    {
        await using var lease = await source.OpenAsync(cancellationToken);
        return await lease.ReadAsync(credentials, cancellationToken);
    }

    /// <summary>健康寫入單獨借一個 lease，完成重驗與 CAS 後確定性歸還。</summary>
    private static async Task WriteHealthAsync(IChannelDirectorySource source, ChannelDirectoryRecord channel, ChannelHealthResult result, bool disable)
    {
        await using var lease = await source.OpenAsync(default);
        await lease.WriteHealthAsync(channel, result, disable, default);
    }

    /// <summary>測試 owner 的正常 SDK 控制；只有一次方法範圍租約，所有輸入查詢會保留供精確斷言。</summary>
    private sealed class SdkEnvironment
    {
        private readonly Guid organization = Guid.NewGuid();
        internal Guid? ServerOrganization;
        internal Entity[] Rows = [];
        internal int PageSize = 100;
        internal bool RepeatPage;
        internal Exception? ReadFault;
        internal Exception? CleanupFault;
        internal Exception? ProbeFault;
        internal Exception? DiagnosticsFault;
        internal Action? BeforeRead;
        internal Action<QueryExpression>? BeforeQuery;
        internal bool ApplyServerConditions;
        internal TaskCompletionSource? ProbeEntered;
        internal TaskCompletionSource? ProbeRelease;
        internal int Acquired;
        internal int Returned;
        internal int Faulted;
        internal List<QueryExpression> Queries { get; } = [];
        internal List<UpdateRequest> Updates { get; } = [];
        internal List<(Exception Failure, string Operation)> Notifications { get; } = [];
        internal IOrganizationService Service { get; }
        internal IChannelDiagnostics Diagnostics { get; }
        internal int WhoAmICalls;
        internal DataverseChannelDirectorySource Source { get; }
        /// <summary>建立嚴格 Async2 替身，WhoAmI、正常查詢及 CAS 都有可成功路徑，不支援非預期操作。</summary>
        internal SdkEnvironment()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var asynchronous = sdk.As<IOrganizationServiceAsync2>();
            Service = sdk.Object;
            asynchronous.Setup(s => s.ExecuteAsync(It.IsAny<OrganizationRequest>(), It.IsAny<CancellationToken>()))
                .Returns<OrganizationRequest, CancellationToken>((request, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (request is WhoAmIRequest)
                    {
                        Interlocked.Increment(ref WhoAmICalls);
                        var identity = new WhoAmIResponse(); identity.Results["OrganizationId"] = ServerOrganization ?? organization;
                        return Task.FromResult<OrganizationResponse>(identity);
                    }
                    var update = Assert.IsType<UpdateRequest>(request);
                    lock (Updates) Updates.Add(update);
                    return Task.FromResult<OrganizationResponse>(new UpdateResponse());
                });
            asynchronous.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryBase>(), It.IsAny<CancellationToken>()))
                .Returns<QueryBase, CancellationToken>(async (raw, ct) =>
                {
                    BeforeRead?.Invoke();
                    ct.ThrowIfCancellationRequested();
                    var query = Assert.IsType<QueryExpression>(raw);
                    lock (Queries) Queries.Add(query);
                    BeforeQuery?.Invoke(query);
                    ct.ThrowIfCancellationRequested();
                    if (IsForeignProbe(query))
                    {
                        ProbeEntered?.TrySetResult();
                        if (ProbeRelease != null) await ProbeRelease.Task.WaitAsync(ct);
                        if (ProbeFault != null) throw ProbeFault;
                    }
                    if (ReadFault != null) throw ReadFault;
                    var id = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "new_linechannelid" && c.Operator == ConditionOperator.Equal)?.Values[0];
                    var values = id is Guid target ? Rows.Where(row => row.Id == target) : Rows.AsEnumerable();
                    if (ApplyServerConditions)
                    {
                        foreach (var condition in query.Criteria.Conditions.Where(condition => condition.AttributeName == "new_product"))
                            values = values.Where(row => MatchesProduct(row, condition));
                        values = values.Select(row =>
                        {
                            var projected = new Entity(row.LogicalName, row.Id);
                            foreach (var attribute in row.Attributes.Where(attribute => query.ColumnSet.Columns.Contains(attribute.Key)))
                                projected.Attributes.Add(attribute.Key, attribute.Value);
                            return projected;
                        });
                    }
                    if (query.TopCount is int topCount)
                    {
                        var selectedTop = values.Take(topCount).ToArray();
                        return new EntityCollection(selectedTop.ToList())
                        {
                            MoreRecords = false,
                            PagingCookie = null
                        };
                    }
                    var size = Math.Min(query.PageInfo.Count, PageSize);
                    var page = RepeatPage ? 1 : query.PageInfo.PageNumber;
                    var selected = values.Skip((page - 1) * size).Take(size).ToArray();
                    return new EntityCollection(selected.ToList())
                    {
                        MoreRecords = RepeatPage || values.Count() > page * size,
                        PagingCookie = "cookie-" + page.ToString(CultureInfo.InvariantCulture)
                    };
                });
            var diagnostics = new Mock<IChannelDiagnostics>(MockBehavior.Strict);
            diagnostics.Setup(value => value.Report(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<Exception, string, CancellationToken>((failure, operation, ct) =>
                {
                    Assert.False(ct.IsCancellationRequested);
                    lock (Notifications) Notifications.Add((failure, operation));
                    if (DiagnosticsFault != null) throw DiagnosticsFault;
                });
            Diagnostics = diagnostics.Object;
            Source = new DataverseChannelDirectorySource("GoodShepherd2", organization, ct =>
            {
                ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref Acquired);
                var lease = new Mock<IClientLease>(MockBehavior.Strict);
                lease.SetupGet(l => l.Service).Returns(sdk.Object);
                lease.Setup(l => l.MarkFaulted()).Callback(() => Interlocked.Increment(ref Faulted));
                lease.Setup(l => l.Dispose()).Callback(() => { Interlocked.Increment(ref Returned); if (CleanupFault != null) throw CleanupFault; });
                return lease.Object;
            }, Diagnostics);
        }
        /// <summary>離線模擬實際產品條件而非回傳整表；不會讀取秘密欄位，未知條件立即使測試失敗。</summary>
        private static bool MatchesProduct(Entity row, ConditionExpression condition)
        {
            var product = row.GetAttributeValue<string>("new_product");
            return condition.Operator switch
            {
                ConditionOperator.Equal => product != null && Equals(product, condition.Values.Single()),
                ConditionOperator.NotEqual => product != null && !Equals(product, condition.Values.Single()),
                ConditionOperator.NotNull => product != null,
                _ => throw new InvalidOperationException("Unexpected synthetic product condition.")
            };
        }
        /// <summary>同名、不同正式 Id 與數字鍵的完整資料，帶真正 SDK 型別而非任意字典 token。</summary>
        internal Entity Row(string key)
        {
            var row = new Entity("new_linechannel", Guid.NewGuid());
            row["new_linechannelid"] = row.Id; row["new_name"] = "合法相同名稱";
            row["new_product"] = "GoodShepherd2"; row["new_channelid"] = key;
            row["new_channelstatus"] = new OptionSetValue(100000001);
            row["versionnumber"] = 1L;
            row["modifiedon"] = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);
            row["new_accesstokenencrypted"] = "synthetic-envelope"; row["new_channelsecretencrypted"] = "synthetic-secret";
            return row;
        }
    }
}
