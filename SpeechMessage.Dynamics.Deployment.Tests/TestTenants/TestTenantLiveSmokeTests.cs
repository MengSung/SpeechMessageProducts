/*
檔案整體說明
目的與定位：F12 測試租戶 Live3P 建立／清理的專屬測試與離線基線守門；只有實機方法帶 Live3P，避免純 helper 測試被一般篩選排除。
主要功能與流程：以固定 goodshepherd20／GoodShepherd2、c201 與 T01 驗證建立前後的 account、contact、可用名額及 LINE 宮格／alias 數；離線方法只使用合成 ID、文字與例外驗證分頁、身份、容量及鎖 owner 拒絕。
使用邊界與維護：實機入口必須先確認三把外部 F12 鎖，再讀固定 Deployment 與 Handoff User Secrets，先驗伺服器才讀狀態。不得以此檔宣稱本輪已連線或通過實機；只允許日後明確選取 Live3P 執行。
資源與失敗：狀態僅屬單次測試，基線只發布計數；不保存名稱或跨測試身份。既有租戶／manifest 不清理，原始及補償失敗需保留但對測試輸出只呈現安全型別；所有借用操作完成後才反序關閉 owner，診斷不寫檔也不發 LINE。
驗證方式：先以尚未實作的守門觀察離線 RED，再實作為 GREEN；交付執行目標專案編譯、排除 Live3P 的聚焦測試及 UTF-8 無 BOM／CRLF 位元組檢查，不以合成結果取代實機計數證據。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Deployment.Schema;
using SpeechMessage.Dynamics.Deployment.TestTenants;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.TestTenants;

/// <summary>F12 單次測試 owner；純 helper 案例不載入設定、不讀環境鎖，也不建立任何遠端 client。</summary>
public sealed class TestTenantLiveSmokeTests
{
    /// <summary>SDK 單頁最多五千筆；遇到下一頁即拒絕，不以 TopCount 截斷假裝整份資料已盤點。</summary>
    private const int MaximumRows = 5000;

    /// <summary>LINE 回應只接受一千筆身份以內，SDK lease 另限制 HTTP 回應大小及十五秒期限。</summary>
    private const int MaximumLineRows = 1000;

    /// <summary>
    /// 在呼叫端已持有 lane=B／wp=F12 三把環境鎖時，驗證固定測試組織的 CLI create／cleanup
    /// 會回到原始 account、contact、可用名額及 T01 宮格／alias 計數。既有 c201 或 manifest
    /// 在任何建立之前拒絕，只有本次建立成功或本次新產生復原 manifest 才能精確補償。
    /// 本案不簽發邀請、不讀 LINE userId、不建立其他組織連線；只在方法上標實機分類。
    /// </summary>
    /// <returns>建立與清理均結束、計數回復且全部 owner 排空後完成；故障只公開安全型別。</returns>
    [Fact]
    [Trait("Category", "Live3P")]
    [Trait("WP", "F12")]
    public async Task CreateThenCleanup_CountsReturnToBaseline()
    {
        var owners = new List<Func<ValueTask>>(12);
        var failures = new List<Exception>();
        ExceptionDiagnostics? diagnostics = null;
        // 真正例外只屬當次測試；不把 Message、Data 或 userId 放進 xUnit 的例外鏈。
        void Report(Exception failure)
        {
            failures.Add(failure);
            _ = diagnostics?.Report(failure, "TestTenantLiveSmokeTests.F12", notify: false);
        }
        try
        {
            // 此步必須早於所有 User Secrets provider 建構；只讀既有鎖，不建立或刪除別人的鎖。
            HoldEnvironmentLocks(owners);
            var repository = DeploymentCli.FindRepositoryRoot();
            var catalog = OrganizationCatalog.Load(repository);
            var target = catalog.GetTest("goodshepherd20");
            if (target.Name != "goodshepherd20" || target.Kind != "test" || string.IsNullOrEmpty(target.Host))
                throw Rejected();
            diagnostics = new ExceptionDiagnostics(Path.Combine(repository, "artifacts", "f12-disabled-diagnostics"),
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: false, sendLine: false));
            owners.Add(diagnostics.DisposeAsync);
            var deadline = Own(new CancellationTokenSource(TimeSpan.FromMinutes(3)), owners);
            var deployment = Own((ConfigurationRoot)DeploymentConfiguration.Create(typeof(UserSecretsOrganizationConnector).Assembly), owners);
            var handoff = Own((ConfigurationRoot)new ConfigurationBuilder()
                .AddUserSecrets("SpeechMessage.ThreeProducts.Handoff", reloadOnChange: false).Build(), owners);
            var configuration = Own(CreateFixedConfiguration(deployment, handoff), owners);
            var channelId = configuration["ThreeProducts:Line:T01:ChannelId"]!;
            var connector = new UserSecretsOrganizationConnector(configuration);
            var control = ConnectVerified(connector, target, owners, deadline.Token);
            var artifactRoot = Path.Combine(repository, "artifacts");
            var manifestPath = Path.Combine(artifactRoot, "test-tenants", "c201.json");
            var existedBefore = ManifestExists(manifestPath);
            if (existedBefore || ReadTenantCount(control, deadline.Token) != 0) throw Rejected();
            var line = Own(new LineMessagingClientLease(configuration["ThreeProducts:Line:T01:ChannelAccessToken"]!), owners);
            var baseline = await ReadBaselineAsync(control, line.Client, deadline.Token);
            // 三把文字鎖的唯讀 handle 維持至整輪排空，防止其他程序在本輪改寫 owner 或替換鎖檔。
            var attempted = false;
            var succeeded = false;
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (ManifestExists(manifestPath) || ReadTenantCount(control, deadline.Token) != 0) throw Rejected();
                attempted = true;
                // CLI 接受數字 ChannelId；T01 是固定 Handoff 別名，不以其他 Channel 作 fallback。
                var result = await TestTenantCli.RunAsync(["test-tenant", "create", "--product", "GoodShepherd2",
                    "--org", "goodshepherd20", "--code", "c201", "--name", "測試F12", "--expect-channel", channelId],
                    catalog, connector, TextWriter.Null, configuration, Report, null, diagnostics, deadline.Token);
                if (result != 0) throw new InvalidOperationException("F12 create did not complete successfully.");
                succeeded = true;
                var manifest = TestTenantManifest.Load(artifactRoot, target.Name, "c201");
                if (manifest.Product != "GoodShepherd2" || ReadTenantCount(control, deadline.Token) != 1) throw Rejected();
            }
            catch (Exception failure) { Report(failure); }
            finally
            {
                // 不共用已逾時的 create token；清理與回復盤點各有獨立期限，送出的 SDK／HTTP 都必須等實際返回。
                try
                {
                    if (ShouldCleanup(attempted, succeeded, existedBefore, ManifestExists(manifestPath)))
                    {
                        using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                        var result = await TestTenantCli.RunAsync(["test-tenant", "cleanup", "--org", "goodshepherd20", "--code", "c201"],
                            catalog, connector, TextWriter.Null, configuration, Report, null, diagnostics, cleanupDeadline.Token);
                        if (result != 0) Report(new InvalidOperationException("F12 cleanup did not complete successfully."));
                        if (ManifestExists(manifestPath)) Report(Rejected());
                    }
                }
                catch (Exception failure) { Report(failure); }
                if (attempted)
                {
                    try
                    {
                        using var verificationDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                        var verification = ConnectVerified(connector, target, owners, verificationDeadline.Token);
                        var after = await ReadBaselineAsync(verification, line.Client, verificationDeadline.Token);
                        if (baseline != after || ReadTenantCount(verification, verificationDeadline.Token) != 0)
                            Report(new InvalidDataException("F12 counts did not return to the complete baseline."));
                    }
                    catch (Exception failure) { Report(failure); }
                }
            }
        }
        catch (Exception failure) { Report(failure); }
        finally
        {
            // 沒有 fire-and-forget：所有 await 都已返回才處置 LINE／SDK，再關組態、期限、診斷與外部鎖 handle。
            for (var index = owners.Count - 1; index >= 0; index--)
            {
                try { await owners[index](); }
                catch (Exception failure) { Report(failure); }
            }
            owners.Clear();
        }
        if (failures.Count != 0) throw SafeAggregate(failures);
    }

    /// <summary>合成不完整回應即使已有合法 ID 仍必須拒絕，防止把第一頁誤當完整基線。</summary>
    [Fact]
    public void BaselineRows_MoreRecords_Refused()
    {
        var rows = new EntityCollection { MoreRecords = true };
        rows.Entities.Add(new Entity("account", Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => CountRows(rows, "account", 2));
    }

    /// <summary>故障注入重複及空正式 ID；不能以去重或忽略空值掩蓋身份衝突。</summary>
    [Fact]
    public void BaselineRows_DuplicateOrEmptyIdentity_Refused()
    {
        var id = Guid.NewGuid();
        var duplicate = new EntityCollection();
        duplicate.Entities.Add(new Entity("account", id));
        duplicate.Entities.Add(new Entity("account", id));
        var empty = new EntityCollection();
        empty.Entities.Add(new Entity("account", Guid.Empty));
        Assert.Throws<InvalidDataException>(() => CountRows(duplicate, "account", 2));
        Assert.Throws<InvalidDataException>(() => CountRows(empty, "account", 2));
    }

    /// <summary>有界候選超量或混入另一實體時拒絕，不能將錯誤表的 ID 算成 account。</summary>
    [Fact]
    public void BaselineRows_CapacityOrWrongEntity_Refused()
    {
        var rows = new EntityCollection();
        rows.Entities.Add(new Entity("account", Guid.NewGuid()));
        rows.Entities.Add(new Entity("account", Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => CountRows(rows, "account", 1));
        Assert.Throws<InvalidDataException>(() => CountRows(rows, "contact", 2));
        Assert.Throws<InvalidDataException>(() => CountRows(null, "account", 2));
    }

    /// <summary>不同正式 ID 即使沒有名稱也應完整保留；空集合應有零筆基線，沒有額外資源需要回收。</summary>
    [Fact]
    public void BaselineRows_DifferentIdsAndEmptyCollection_CountExactly()
    {
        var rows = new EntityCollection();
        rows.Entities.Add(new Entity("account", Guid.NewGuid()));
        rows.Entities.Add(new Entity("account", Guid.NewGuid()));
        Assert.Equal(2, CountRows(rows, "account", 2));
        Assert.Equal(0, CountRows(new EntityCollection(), "account", 2));
    }

    /// <summary>LINE provider 身份必須非空、唯一且有界；只注入合成字串，不建立 HTTP 或讀取宮格名稱。</summary>
    [Fact]
    public void BaselineLineIds_EmptyDuplicateOrCapacity_Refused()
    {
        Assert.Throws<InvalidDataException>(() => CountLineIds(["menu-a", "menu-a"], 2));
        Assert.Throws<InvalidDataException>(() => CountLineIds([" "], 2));
        Assert.Throws<InvalidDataException>(() => CountLineIds([null], 2));
        Assert.Throws<InvalidDataException>(() => CountLineIds(["menu-a", "menu-b"], 1));
        Assert.Equal(2, CountLineIds(["menu-a", "menu-b"], 2));
    }

    /// <summary>鎖文字不能以子字串、大小寫、重複欄位、錯誤工作包或其他線假冒；測試只解析合成文字。</summary>
    [Fact]
    public void LockOwner_MissingWrongOrAmbiguousTokens_Refused()
    {
        string?[] rejected = [null, "", "lane=A wp=F12", "lane=B wp=F120", "lane=b wp=F12",
            "otherlane=B wp=F12", "lane=B", "wp=F12", "lane=B lane=A wp=F12",
            "lane=B wp=F12 wp=F12", "lane=B wp=F12 # owner", "lane=B wp=F12 start=invalid"];
        foreach (var text in rejected)
            Assert.Throws<InvalidDataException>(() => RequireLockOwner(text));
    }

    /// <summary>固定 owner 可含既有 round-trip start 時間並允許空白分隔；不檢查或接觸本機真實鎖。</summary>
    [Fact]
    public void LockOwner_ExactTokensWithOptionalTimestamp_Accepted()
    {
        RequireLockOwner("lane=B wp=F12");
        RequireLockOwner("wp=F12\r\nlane=B start=2026-11-28T14:00:00.0000000+08:00\r\n");
    }

    /// <summary>只有原本無 manifest 且本輪嘗試建立，才可因成功或新 manifest 補償；既有證據永不清除。</summary>
    [Fact]
    public void CleanupDecision_PreexistingManifestAndUnattemptedCreate_Refused()
    {
        Assert.False(ShouldCleanup(false, false, false, true));
        Assert.False(ShouldCleanup(true, true, true, true));
        Assert.False(ShouldCleanup(true, false, true, true));
        Assert.False(ShouldCleanup(true, false, false, false));
        Assert.True(ShouldCleanup(true, true, false, false));
        Assert.True(ShouldCleanup(true, false, false, true));
    }

    /// <summary>合成敏感本文的主要與清理例外必須保留同一實例，但 AggregateException 及子例外輸出不可展開該本文。</summary>
    [Fact]
    public void FailureProjection_PreservesOriginalsWithoutExposingMessages()
    {
        var primary = new IOException("SYNTHETIC_PRIVATE_PRIMARY");
        var cleanup = new InvalidOperationException("SYNTHETIC_PRIVATE_CLEANUP");
        var aggregate = SafeAggregate([primary, cleanup]);
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.DoesNotContain("SYNTHETIC_PRIVATE", aggregate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SYNTHETIC_PRIVATE", aggregate.Message, StringComparison.Ordinal);
        Assert.Same(primary, Assert.IsType<SafeFailure>(aggregate.InnerExceptions[0]).Original);
        Assert.Same(cleanup, Assert.IsType<SafeFailure>(aggregate.InnerExceptions[1]).Original);
        Assert.All(aggregate.InnerExceptions, failure => Assert.Null(failure.InnerException));
    }

    /// <summary>
    /// 完整單頁才計數；空回應、下一頁、超過核定容量及空／重複／錯表正式 ID 都拒絕。
    /// HashSet 只在此框架暫存身份，不按姓名去重、不修改 SDK 回應，回傳後只留下數字。
    /// </summary>
    private static int CountRows(EntityCollection? rows, string entity, int maximum)
    {
        if (maximum is < 1 or > MaximumRows || rows is null || rows.MoreRecords || rows.Entities.Count > maximum)
            throw Rejected();
        var identities = new HashSet<Guid>();
        foreach (var row in rows.Entities)
            if (row is null || row.LogicalName != entity || row.Id == Guid.Empty || !identities.Add(row.Id)) throw Rejected();
        return identities.Count;
    }

    /// <summary>逐筆有界驗證 LINE provider ID，不讀名稱、不靜默去重；枚舉器由 foreach 確定性關閉。</summary>
    private static int CountLineIds(IEnumerable<string?> ids, int maximum)
    {
        if (maximum is < 1 or > MaximumLineRows) throw Rejected();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (identities.Count == maximum || string.IsNullOrWhiteSpace(id) || id.Length > 128 || !identities.Add(id)) throw Rejected();
        return identities.Count;
    }

    /// <summary>
    /// 精確解析既有鎖的 lane／wp token，兩者各一次；可附一個合法 round-trip start。
    /// 子字串、大小寫替代、重複欄位及未知文字均拒絕，錯誤不回顯鎖的原始本文。
    /// </summary>
    private static void RequireLockOwner(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) throw Rejected();
        var lane = false;
        var workPackage = false;
        var start = false;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "lane=B" && !lane) lane = true;
            else if (token == "wp=F12" && !workPackage) workPackage = true;
            else if (token.StartsWith("start=", StringComparison.Ordinal) && !start &&
                DateTimeOffset.TryParseExact(token[6..], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) start = true;
            else throw Rejected();
        }
        if (!lane || !workPackage) throw Rejected();
    }

    /// <summary>既有 manifest 永不補償；只有本輪已送 create 且成功或新復原證據出現才授權精確 cleanup。</summary>
    private static bool ShouldCleanup(bool attempted, bool succeeded, bool existedBefore, bool existsNow) =>
        attempted && !existedBefore && (succeeded || existsNow);

    /// <summary>
    /// 三個固定檔案先拒絕 link，再以最多 4KiB 的嚴格 UTF-8 讀取；允許既有 PowerShell UTF-8 BOM。
    /// 每個 handle 一開啟就登記唯一 owner，只允許別人讀，不允許寫入或刪除替換；部分失敗仍由外層反序關閉。
    /// 這只是核對外部持鎖協定，不創造授權、不取得或刪除鎖；檔案不存在時在秘密載入前拒絕。
    /// </summary>
    private static void HoldEnvironmentLocks(List<Func<ValueTask>> owners)
    {
        foreach (var name in new[] { "test-env-d365.lock", "test-env-sql.lock", "test-env-line.lock" })
        {
            var path = Path.Combine(@"C:\ProgramData\SpeechMessage\locks", name);
            SchemaDeploymentCli.EnsureNoLinks(path);
            var stream = Own(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), owners);
            if (stream.Length is <= 0 or > 4096) throw Rejected();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096, leaveOpen: true);
            RequireLockOwner(reader.ReadToEnd().TrimStart('\uFEFF'));
        }
    }

    /// <summary>
    /// 只投影 goodshepherd20 的三個部署憑證鍵、T01 的兩個 LINE 鍵及本產品 Scheduling 鍵。
    /// 不列舉 Handoff、不讀 T02 或其他產品，不載入環境變數／appsettings；缺清理必要值在建立前拒絕。
    /// 字串不能抹除，僅在此輪 provider／SDK 存活期間保留參照；不存靜態或回顯設定值。
    /// </summary>
    private static ConfigurationRoot CreateFixedConfiguration(ConfigurationRoot deployment, ConfigurationRoot handoff)
    {
        var channel = Required(handoff, "ThreeProducts:Line:T01:ChannelId");
        if (channel.Length is < 10 or > 20 || !channel.All(char.IsAsciiDigit)) throw Rejected();
        var schedulingKey = "TestTenants:Products:GoodShepherd2:SchedulingConnectionString";
        var scheduling = deployment[schedulingKey] ?? handoff[schedulingKey];
        if (string.IsNullOrWhiteSpace(scheduling) || scheduling.Length > 8192) throw Rejected();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Deployment:Organizations:goodshepherd20:OrganizationUrl"] = Required(deployment, "Deployment:Organizations:goodshepherd20:OrganizationUrl"),
            ["Deployment:Organizations:goodshepherd20:Username"] = Required(deployment, "Deployment:Organizations:goodshepherd20:Username"),
            ["Deployment:Organizations:goodshepherd20:Password"] = Required(deployment, "Deployment:Organizations:goodshepherd20:Password"),
            ["ThreeProducts:Line:T01:ChannelId"] = channel,
            ["ThreeProducts:Line:T01:ChannelAccessToken"] = Required(handoff, "ThreeProducts:Line:T01:ChannelAccessToken"),
            ["TestTenants:Products:GoodShepherd2:ChannelKey"] = channel,
            [schedulingKey] = scheduling
        };
        try { return (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build(); }
        finally { values.Clear(); }
    }

    /// <summary>固定鍵值缺漏或超出單一設定容量即拒絕；訊息不包含鍵值或秘密原文。</summary>
    private static string Required(ConfigurationRoot configuration, string key)
    {
        var value = configuration[key];
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 8192 ? value : throw Rejected();
    }

    /// <summary>
    /// 每次盤點以單命令獨占租約驗伺服器後才交出借用服務；每次 SDK 最多三十秒。
    /// 回復驗證另建新租約，避免沿用先前可能已逾時／取消的控制連線；處置仍由整輪反序 owner 執行。
    /// </summary>
    private static IOrganizationService ConnectVerified(UserSecretsOrganizationConnector connector, OrganizationDefinition target,
        List<Func<ValueTask>> owners, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var lease = Own(connector.Connect(target.Name), owners);
        if (lease.Service is not OnPremiseClient sdk) throw Rejected();
        sdk.Timeout = TimeSpan.FromSeconds(30);
        ProductionGuard.VerifyServer(lease.Service, target, ct);
        return lease.Service;
    }
    /// <summary>固定實體只取主鍵，使用單頁容量而非 TopCount 截斷；名額只數 new_slotstatus=100000000。</summary>
    private static QueryExpression CountQuery(string entity, int maximum = MaximumRows)
    {
        if (entity is not ("account" or "contact" or "new_channelslot") || maximum is < 1 or > MaximumRows) throw Rejected();
        var query = new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(entity + "id"),
            PageInfo = new PagingInfo { Count = maximum, PageNumber = 1 }
        };
        if (entity == "new_channelslot") query.Criteria.AddCondition("new_slotstatus", ConditionOperator.Equal, 100000000);
        return query;
    }

    /// <summary>c201 存在性查詢最多兩筆且只取 ID；任何分頁／重複身份仍拒絕，不讀取姓名。</summary>
    private static int ReadTenantCount(IOrganizationService service, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var query = CountQuery("account", 2);
        query.Criteria.AddCondition("new_tenantcode", ConditionOperator.Equal, "c201");
        var rows = service.RetrieveMultiple(query);
        ct.ThrowIfCancellationRequested();
        return CountRows(rows, "account", 2);
    }

    /// <summary>
    /// 借入已驗伺服器的 SDK 與 T01 LINE client，逐項完整查詢後才發布不可變五項計數。
    /// HTTP 不使用 WaitAsync 丟棄在途工作，靠 lease 的十五秒期限等待返回；取消前後檢查，owner 最後才關閉。
    /// LINE SDK 會解析整個宮格回應，但此檔只讀 RichMenuId／alias ID，不讀取或輸出顯示名稱。
    /// </summary>
    private static async Task<Baseline> ReadBaselineAsync(IOrganizationService service, ILineMessagingClient line, CancellationToken ct)
    {
        int Count(string entity)
        {
            ct.ThrowIfCancellationRequested();
            var rows = service.RetrieveMultiple(CountQuery(entity));
            ct.ThrowIfCancellationRequested();
            return CountRows(rows, entity, MaximumRows);
        }
        var accounts = Count("account");
        var contacts = Count("contact");
        var available = Count("new_channelslot");
        ct.ThrowIfCancellationRequested();
        var menus = await line.GetRichMenuListAsync();
        ct.ThrowIfCancellationRequested();
        if (menus is null || menus.Count > MaximumLineRows) throw Rejected();
        var menuCount = CountLineIds(menus.Select(menu => menu?.RichMenuId), MaximumLineRows);
        ct.ThrowIfCancellationRequested();
        var aliases = await line.GetRichMenuAliasListAsync();
        ct.ThrowIfCancellationRequested();
        if (aliases?.Aliases is null || aliases.Aliases.Count > MaximumLineRows) throw Rejected();
        var aliasCount = CountLineIds(aliases.Aliases.Select(alias => alias?.RichMenuAliasId), MaximumLineRows);
        return new Baseline(accounts, contacts, available, menuCount, aliasCount);
    }

    /// <summary>檢查真正 repo 的固定 manifest 路徑；只把不存在視為 false，權限或 IO 故障不能冒充不存在。</summary>
    private static bool ManifestExists(string path)
    {
        SchemaDeploymentCli.EnsureNoLinks(path);
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    /// <summary>每個同步 owner 在取得後立即登記；所有已借出的操作返回後，由外層逐個回收且收集處置失敗。</summary>
    private static T Own<T>(T resource, List<Func<ValueTask>> owners) where T : IDisposable
    {
        owners.Add(() => { resource.Dispose(); return ValueTask.CompletedTask; });
        return resource;
    }

    /// <summary>基線只包含五個無身份計數，單次測試內比較；不保留 Entity、HTTP 回應或憑證參照。</summary>
    /// <param name="Accounts">已完整驗證的 account 總數。</param>
    /// <param name="Contacts">已完整驗證的 contact 總數。</param>
    /// <param name="AvailableSlots">可用狀態的 new_channelslot 數。</param>
    /// <param name="RichMenus">固定 T01 Channel 的唯一宮格 ID 數。</param>
    /// <param name="Aliases">固定 T01 Channel 的唯一 alias ID 數。</param>
    private sealed record Baseline(int Accounts, int Contacts, int AvailableSlots, int RichMenus, int Aliases);

    /// <summary>保留每個真正故障實例至測試結束，但標準 AggregateException 鏈只接安全葉節點。</summary>
    private static AggregateException SafeAggregate(IEnumerable<Exception> failures) =>
        new("F12 create, cleanup or resource drain failed; sensitive details suppressed.", failures.Select(failure => new SafeFailure(failure)));

    /// <summary>固定拒絕只表示安全前置／完整基線不成立，不回顯遠端回應或使用者輸入。</summary>
    private static InvalidDataException Rejected() => new("F12 live smoke safety or complete baseline contract was refused.");

    /// <summary>安全例外葉節點只公開型別摘要；原始物件保持在內部欄位，不能放入 InnerException。</summary>
    private sealed class SafeFailure : Exception
    {
        /// <summary>保留當次原始故障供記憶體內辨識；不交給 xUnit 的標準 InnerException 展開。</summary>
        internal Exception Original { get; }

        /// <summary>原始故障只活到當次測試結束，公開訊息不讀取任何 SDK Message 或 Data。</summary>
        internal SafeFailure(Exception original) : base("F12 failure type: " + original.GetType().Name) => Original = original;
    }
}
