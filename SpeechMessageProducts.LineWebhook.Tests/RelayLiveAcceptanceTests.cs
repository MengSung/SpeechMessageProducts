/*
 * 檔案整體說明
 * 目的與定位：P7／P8唯一測試組織代送實機驗收，Category=P7Live與P8Live
 * 由一般runsettings排除；P8Live另驗證 qps-ploc 語系租約的回復。
 * 主要流程：只讀Deployment User Secrets，先核固定HTTPS服務與server uniqueName，再驗D34唯一
 * 測試綁定及全Channel均為測試token；確認沒有其他待辦，建立本次SMV-TEST-fee與第四類待辦。
 * P8 customization 案例另透過核心 ChurchSettingsService 對 A 寫入唯一覆寫及奉獻類別，
 * 由 LineWebhook 真實 source/cache 組合載入 A、B 快照，比對組字後才實際代送一次。
 * TextContext.Organization 使用 server uniqueName goodshepherdback；WhoAmI GUID 只供身分核對，
 * 不可代替工廠要求的固定部署組織識別。
 * 先等待兩分鐘確認測試IIS未認領，再啟動本機正式註冊的唯一代送worker，60秒內完成、用量+1。
 * 使用邊界與維護：不啟動HTTP／付款／QR，不改IIS／Node，不讀正式組織。資料寫入前把候選
 * 正式ID與測試marker落ignored ownership檔，失敗亦先停止worker排空再依精確ID清理及核零。
 * 資源與隔離：fixture擁有控制SDK、DI容器、設定根、HTTP client與key；每筆worker使用新scope SDK，
 * 金鑰只從User Secrets進記憶體，finally清零且不輸出任何憑證或回應本文；查詢有界，未知值拒絕。
 * 主機設定由fixture持有至worker停止及DI scope釋放之後，例外退出不能提前關閉在用設定。
 * 驗證證據只輸出筆數、狀態、用量差與零殘留；前置失敗另輸出固定原因碼，不回顯例外本文。
 * 實機執行需明確Category篩選，離線不冒稱成功。
 */
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using LineMessagingProcessor.AspNetCore.Webhooks;
using SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;
using LineMessagingProcessor.Core.Security;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessage.Hosting.Outbound;
using SpeechMessage.Tenancy;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using SpeechMessageProducts.GoodShepherd.Core.Settings;
using ToolUtilityNameSpace.ConnectionOperations;
using Xunit;
using Xunit.Abstractions;
namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>只經明確P7Live啟動的真測試組織與測試Channel驗收。</summary>
public sealed class RelayLiveAcceptanceTests(ITestOutputHelper output)
{
    /// <summary>真worker完成一次繳費通知，待辦Completed且用量剛好+1，最後測試fee/task精確清零。</summary>
    [Fact]
    [Trait("Category", "P7Live")]
    public async Task Live_Relay_PushesOnceToTestContact()
    {
        await using var f = new LiveOwner(output);
        await f.RunAsync();
    }

    /// <summary>
    /// 在核准測試組織暫時套用 qps-ploc 後執行真正的 FeeNotify 主機代送，
    /// 驗證語系租約可回復、工作在 60 秒內完成且實機清理不留測試資料。
    /// LINE 訊息本文由離線組合測試保存決定性格式證據；本案不把用量差
    /// 誤稱為本文回讀。
    /// </summary>
    [Fact]
    [Trait("Category", "P8Live")]
    public async Task Pseudo_Step3_FeeNotifyWithin60s_PseudoAndFormatted()
    {
        await using var f = new LiveOwner(output, pseudoLocale: true);
        await f.RunAsync();
    }

    /// <summary>
    /// 以 ChurchSettingsService 寫入核准測試教會 A 的偽語系範本及奉獻類別，
    /// 由真 LineWebhook Dataverse source/cache 載入 A 與臨時 B，證明組字隔離後只代送一次。
    /// </summary>
    [Fact]
    [Trait("Category", "P8Live")]
    public async Task Customization_Step5_LineOverrideWithin60s_ChurchBUnaffected()
    {
        await using var f = new LiveOwner(output, pseudoLocale: true);
        await f.RunCustomizationAsync();
    }

    /// <summary>
    /// 只讀載入核准測試教會的既有文字覆寫快照，用於定位 P8Live 前置的來源失敗；
    /// 不修改教會／會友／類別，不啟動 LINE worker、不連 LINE API，並釋放完整 Host owner。
    /// </summary>
    [Fact]
    [Trait("Category", "P8Live")]
    public async Task Customization_ReadOnlyOverrideSourceLoad()
    {
        await using var f = new LiveOwner(output, pseudoLocale: false);
        await f.ProbeCustomizationCacheReadOnlyAsync();
    }

    /// <summary>
    /// 保護真實 LineWebhook 組合根使用產品程式集身分時可在無外部連線下建置；
    /// 使用合成組態並只建立／釋放 Host，不啟動 hosted service 或解析 Dataverse manager。
    /// </summary>
    [Fact]
    public async Task CustomizationHostComposition_ProductApplicationName_BuildsOffline()
    {
        var root = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln")))
            root = root.Parent;
        var repository = root?.FullName ?? throw new InvalidOperationException("Worktree not found.");
        var productRoot = Path.Combine(repository, "SpeechMessageProducts.LineWebhook");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ApplicationName = typeof(global::SpeechMessageProducts.LineWebhook.Program).Assembly.GetName().Name,
            ContentRootPath = productRoot,
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:Organization"] = "SyntheticOrg",
            ["Localization:ChurchOverrides:Enabled"] = "true",
            ["Localization:AllowPseudoLocale"] = "true",
            ["Messaging:NotifyRelay:Enabled"] = "true",
            ["ExceptionNotifications:SendLine"] = "false"
        });

        WebApplication? app = null;
        try
        {
            app = global::SpeechMessageProducts.LineWebhook.Program.BuildApplication(builder);
            Assert.NotNull(app);
        }
        finally
        {
            if (app is not null)
                await app.DisposeAsync();
            else
                ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>註冊檔的映像type不是CLR型別；故意混入PreImage，安全盤點只能取步驟／活動與legacy的完整CLR名稱。</summary>
    [Fact]
    public void RelayLiveSafety_ManifestImageTypes_AreNotClrNamespaces()
    {
        var manifest=JObject.Parse("{\"assemblies\":[{\"steps\":[{\"type\":\"Synthetic.Plugin\",\"images\":[{\"type\":\"PreImage\"}],\"legacy\":[{\"type\":\"Retired.Plugin\"}]}]}]}");
        var types=ReadClrTypes(manifest);
        Assert.Equal(2,types.Length);Assert.Contains("Synthetic.Plugin",types);Assert.Contains("Retired.Plugin",types);
        Assert.DoesNotContain("PreImage",types);
    }
    /// <summary>SDK保存的payload讀回為JSON字串；往返GUID必須正確解析，錯值拒絕，不能讓成功推播後清理失敗。</summary>
    [Fact]
    public void RelayLiveSafety_PayloadRecordIdentity_ParsesJsonString()
    {
        var id = Guid.NewGuid();
        var payload = JObject.Parse(new JObject { ["recordId"] = id }.ToString());
        Assert.True(OwnsRecord(payload, id));
        Assert.False(OwnsRecord(payload, Guid.NewGuid()));
        Assert.False(OwnsRecord(JObject.Parse("{\"recordId\":\"invalid\"}"), id));
    }
    /// <summary>
    /// 保護實機清理流程的失敗隔離契約：一項還原失敗不得阻止後續獨立資料清理；
    /// 以合成例外注入故障，斷言後續 owner 確實執行且失敗被保留供最後彙總。
    /// </summary>
    [Fact]
    public async Task CleanupFailure_DoesNotSkipFollowingCleanupStep()
    {
        var failures = new List<Exception>();
        var followingCleanupRan = false;

        await TryCleanupAsync(
            () => throw new InvalidOperationException("synthetic cleanup failure"),
            failures);
        await TryCleanupAsync(() =>
        {
            followingCleanupRan = true;
            return Task.CompletedTask;
        }, failures);

        Assert.True(followingCleanupRan);
        Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(failures[0]);
    }
    /// <summary>保護實機失敗診斷只輸出固定階段與例外型別，不輸出含機密的例外本文。</summary>
    [Fact]
    public void CustomizationLiveFailure_ReportsStageWithoutExceptionMessage()
    {
        const string sensitiveBody = "SENSITIVE-LIVE-FAILURE";
        var summary = FormatCustomizationFailure("override-save",
            new InvalidOperationException(sensitiveBody));

        Assert.Contains("P8LIVE-CUSTOMIZATION-FAILED stage=override-save", summary, StringComparison.Ordinal);
        Assert.Contains("type=InvalidOperationException", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveBody, summary, StringComparison.Ordinal);
    }

    /// <summary>合成含敏感本文的前置例外，斷言固定原因碼可見而訊息本文不外洩；不建立fixture或連線。</summary>
    [Fact]
    public void RelayPrecheck_Failure_ReportsReasonCode()
    {
        const string sensitiveBody = "SENSITIVE-PRECHECK-MESSAGE";
        var summary = FormatPrecheckFailure(new InvalidOperationException(sensitiveBody));

        Assert.Contains("P7LIVE-PRECHECK-FAILED", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveBody, summary, StringComparison.Ordinal);
    }

    /// <summary>集中格式化安全前置摘要；輸出只含固定碼與例外型別，不保留或回傳例外物件及本文。</summary>
    private static string FormatPrecheckFailure(Exception exception) =>
        "P7LIVE-PRECHECK-FAILED type=" + exception.GetType().Name + "; details suppressed.";

    /// <summary>只以呼叫端固定階段代碼及例外型別診斷實機失敗，不保留或輸出例外本文。</summary>
    private static string FormatCustomizationFailure(string stage, Exception exception) =>
        "P8LIVE-CUSTOMIZATION-FAILED stage=" + stage + " type=" + exception.GetType().Name
        + " target=" + (exception.TargetSite is { } target
            ? (target.DeclaringType?.Name ?? "(unknown-type)") + "." + target.Name
            : "(unknown-target)") + "; details suppressed.";

    /// <summary>只驗正式來源ID，不以文字標記替代資料列身分；未知或畸形值拒絕刪除。</summary>
    private static bool OwnsRecord(JObject payload, Guid id) =>
        payload["recordId"] is { Type: JTokenType.String } token &&
        Guid.TryParseExact(token.Value<string>(), "D", out var actual) && actual != Guid.Empty && actual == id;
    /// <summary>只接真CLR完整名稱，不把image kind等同名JSON欄位拿去算命名空間；集合只活當次安全盤點。</summary>
    private static string[] ReadClrTypes(JObject manifest)=>manifest.SelectTokens("$..type").Values<string>()
        .Where(value=>!string.IsNullOrWhiteSpace(value)&&value.IndexOf('.',StringComparison.Ordinal)>0).Select(value=>value!).Distinct(StringComparer.Ordinal).ToArray();
    /// <summary>
    /// 執行一個獨立清理步驟並只保存例外物件供呼叫端判斷；不得輸出例外本文，
    /// 呼叫端仍須在所有可安全執行的步驟完成後拒絕宣稱清理成功。
    /// </summary>
    private static async Task TryCleanupAsync(Func<Task> cleanup, List<Exception> failures)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    /// <summary>唯一操作owner；所有秘密只在記憶體與既有User Secrets，不進命令列或證據。</summary>
    private sealed class LiveOwner : IAsyncDisposable
    {
        private const string OrganizationName = "goodshepherdback";
        private const string Prefix = "Deployment:Organizations:goodshepherdback:";
        private readonly ITestOutputHelper output;
        private readonly IConfigurationRoot configuration;
        private readonly byte[] key;
        private readonly IOrganizationService sdk;
        private readonly Guid church, contact, fee = Guid.NewGuid(), instance = Guid.NewGuid();
        private Guid organizationId;
        private readonly string idempotencyKey;
        private Guid? task;
        private readonly string marker;
        private readonly bool pseudoLocale;
        private Guid? customizationChurchB, customizationCategoryId;
        private bool customizationBCreateAttempted, customizationOverrideMutationAttempted, customizationCategoryMutationAttempted;
        private string? customizationChurchBName, customizationCategoryName;
        private bool cleanupAttempted;
        private string? originalCustomizationTemplate;
        private bool originalCustomizationTemplateExists;
        private ChurchSettingsService? customizationSettings;
        private WebApplication? customizationApp;
        private string? originalPreferredLanguage;
        private bool preferredLanguageAttempted;
        private ServiceProvider? provider; private OutboundTaskWorker? worker;
        private IConfigurationRoot? hostConfiguration;
        private readonly HttpClient http = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        private readonly string root;
        private bool feeAttempted;
        /// <summary>建構只讀固定User Secrets與端點，失敗立即釋放已建立owner；不在錯誤回顯值。</summary>
        internal LiveOwner(ITestOutputHelper output, bool pseudoLocale = false)
        {
            this.output = output;
            this.pseudoLocale = pseudoLocale;
            marker = (pseudoLocale ? "SMV-TEST-P8-Relay-" : "SMV-TEST-P7-Relay-") + Guid.NewGuid().ToString("N");
            configuration = new ConfigurationBuilder().AddUserSecrets("SpeechMessage.Dynamics.Deployment.P0").Build();
            key = []; sdk = null!;
            try
            {
                root = FindRoot(); var uri = new Uri(Required("OrganizationUrl"));
                if (uri.Scheme != "https" || uri.Host != "goodshepherdback.speechmessage.com.tw" || uri.AbsolutePath != "/XRMServices/2011/Organization.svc" || !uri.IsDefaultPort || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new InvalidOperationException("P7 endpoint rejected.");
                church = Guid.Parse(Required("ShadowTestChurchId")); contact = Guid.Parse(Required("ShadowTestContactId")); key = Convert.FromBase64String(Required("TokenKey"));
                if (key.Length != 64) throw new InvalidOperationException("P7 key size rejected.");
                idempotencyKey = $"{church:N}-100000003-{DeterministicGuid.Create("notify.relay.v1", 3, fee, 1, 0, instance):N}";
                sdk = OpenSdk(); VerifySafety();
            }
            catch (Exception exception)
            {
                output.WriteLine(FormatPrecheckFailure(exception));
                (sdk as IDisposable)?.Dispose(); Array.Clear(key); http.Dispose(); ((IDisposable)configuration).Dispose();
                throw new InvalidOperationException("P7 live setup failed; sensitive details suppressed.");
            }
        }
        /// <summary>無其他工作與D34安全才寫測試資料，所有候選識別在Create前已落ownership。</summary>
        internal async Task RunAsync()
        {
            var directory = Path.Combine(root, "artifacts", pseudoLocale ? "p8" : "p7",
                "relay-live-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)); Directory.CreateDirectory(directory);
            var ownership = Path.Combine(directory, "ownership.json");
            void Save() => File.WriteAllText(ownership, new JObject { ["feeId"] = fee, ["taskId"] = task, ["marker"] = marker, ["idempotencyKey"] = idempotencyKey }.ToString(), new UTF8Encoding(false));
            Save();
            try
            {
                if (pseudoLocale)
                {
                    var current = sdk.Retrieve("contact", contact, new ColumnSet("new_preferredlanguage"));
                    if (current.Id != contact || current.LogicalName != "contact")
                        throw new InvalidOperationException("P8 pseudo contact identity rejected.");
                    originalPreferredLanguage = current.GetAttributeValue<string>("new_preferredlanguage");
                    preferredLanguageAttempted = true;
                    sdk.Update(new Entity("contact", contact) { ["new_preferredlanguage"] = "qps-ploc" });
                    var changed = sdk.Retrieve("contact", contact, new ColumnSet("new_preferredlanguage"));
                    if (!string.Equals(changed.GetAttributeValue<string>("new_preferredlanguage"), "qps-ploc", StringComparison.Ordinal))
                        throw new InvalidOperationException("P8 pseudo language lease was not applied.");
                }
                var pending = sdk.RetrieveMultiple(new QueryExpression("new_outboundtask") { TopCount = 2, ColumnSet = new ColumnSet(false) });
                if (pending.MoreRecords || pending.Entities.Count != 0) throw new InvalidOperationException("P7 requires empty outbound baseline.");
                var row = new Entity("new_fee", fee) { ["new_name"] = marker, ["new_contact_new_fee"] = new EntityReference("contact", contact), ["new_pay_date"] = DateTime.UtcNow, ["new_fee_really_paid"] = new Microsoft.Xrm.Sdk.Money(100), ["new_pay_way"] = new OptionSetValue(100000000) };
                feeAttempted = true; if (sdk.Create(row) != fee) throw new InvalidOperationException("P7 fee identity mismatch.");
                task = MemberNotifyRelay.Enqueue(sdk, MemberNotifyRelayKind.FeeNotify, fee, 1, 0, instance); Save();
                if (!task.HasValue) throw new InvalidOperationException("P7 relay did not enqueue.");
                output.WriteLine("Relay queued; checking competing IIS consumer for 120 seconds.");
                for (var index = 0; index < 24; index++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)); var current = ReadTask();
                    if (current.GetAttributeValue<OptionSetValue>("new_taskstatus")?.Value != 100000000 || current.GetAttributeValue<int>("new_attempts") != 0) throw new InvalidOperationException("Unexpected competing outbound consumer detected.");
                }
                var before = await UsageAsync();
                var values = new Dictionary<string, string?> { ["Messaging:NotifyRelay:Enabled"] = "true", [Prefix + "TokenKey"] = Required("TokenKey") };
                hostConfiguration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
                var hostConfig = hostConfiguration;
                var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(hostConfig); services.AddLogging();
                services.AddScoped<IOrganizationService>(_ => OpenSdk()); services.AddSingleton<IWebhookEventDeduplicator>(new NoDedupe()); services.AddSingleton<IWebhookEventQueue>(new NoQueue());
                services.AddGoodShepherdLineWebhook(hostConfig); provider = services.BuildServiceProvider(validateScopes: true);
                worker = provider.GetServices<IHostedService>().OfType<OutboundTaskWorker>().Single();
                await worker.StartAsync(CancellationToken.None); var deadline = DateTime.UtcNow.AddSeconds(60);
                while (true)
                {
                    var current = ReadTask(); var state = current.GetAttributeValue<OptionSetValue>("new_taskstatus")?.Value;
                    if (state == 100000002) break;
                    if (state == 100000003 || current.GetAttributeValue<int>("new_attempts") != 0 || DateTime.UtcNow >= deadline) throw new InvalidOperationException("P7 relay completion failed.");
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }
                await worker.StopAsync(CancellationToken.None);
                var after = await UsageAsync(); if (after - before != 1) throw new InvalidOperationException("P7 test Channel usage difference was not one.");
                output.WriteLine("Live relay: Completed; attempts=0; test Channel usage delta=1.");
            }
            catch (Exception exception)
            {
                output.WriteLine(FormatPrecheckFailure(exception));
                throw new InvalidOperationException("P7 relay live acceptance failed; consult safe operation evidence.");
            }
            finally { await CleanupAsync(); }
        }

        /// <summary>
        /// 以正式核心服務寫入 A 的教會設定，載入真實 LineWebhook 覆寫來源後核對 A/B 組字，
        /// 再以既有唯一代送 worker 推播。所有資料在 finally 依正式 ID 還原或清理。
        /// </summary>
        internal async Task RunCustomizationAsync()
        {
            var directory = Path.Combine(root, "artifacts", "p8",
                "relay-customization-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            var ownership = Path.Combine(directory, "ownership.json");
            customizationChurchB = Guid.NewGuid();
            customizationCategoryName = "P8 測試奉獻類別 " + Guid.NewGuid().ToString("N")[..8];
            customizationChurchBName = "SMV-TEST-P8-Customization-B-" + customizationChurchB.Value.ToString("N");
            var template = "［P8 A自訂通知 {member} {date} {category} {others} {payway} {amount}ǂ］";
            var stage = "ownership-initialize";
            void Save() => File.WriteAllText(ownership, new JObject
            {
                ["feeId"] = fee,
                ["taskId"] = task,
                ["churchBId"] = customizationChurchB,
                ["categoryId"] = customizationCategoryId,
                ["marker"] = marker,
                ["categoryName"] = customizationCategoryName,
                ["overrideKey"] = "Fees.Notify.Template",
                ["overrideCulture"] = "qps-ploc"
            }.ToString(), new UTF8Encoding(false));

            Save();
            try
            {
                stage = "contact-language";
                SetPseudoContactLanguage();
                stage = "outbound-baseline";
                var pending = sdk.RetrieveMultiple(new QueryExpression("new_outboundtask")
                {
                    TopCount = 2,
                    ColumnSet = new ColumnSet(false)
                });
                if (pending.MoreRecords || pending.Entities.Count != 0)
                    throw new InvalidOperationException("P8 customization requires empty outbound baseline.");

                var churchB = new Entity("account", customizationChurchB.Value)
                {
                    ["name"] = customizationChurchBName
                };
                stage = "church-b-create";
                customizationBCreateAttempted = true;
                if (sdk.Create(churchB) != customizationChurchB.Value)
                    throw new InvalidOperationException("P8 temporary church identity mismatch.");
                stage = "church-b-stamp";
                sdk.Update(new Entity("account", customizationChurchB.Value)
                {
                    ["new_textchangedon"] = DateTime.UtcNow
                });

                var overrideQuery = new QueryExpression("new_localizedtext")
                {
                    TopCount = 2,
                    ColumnSet = new ColumnSet("new_localizedtextid", "new_accountid", "new_key", "new_culture", "new_text")
                };
                overrideQuery.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, church);
                overrideQuery.Criteria.AddCondition("new_key", ConditionOperator.Equal, "Fees.Notify.Template");
                overrideQuery.Criteria.AddCondition("new_culture", ConditionOperator.Equal, "qps-ploc");
                stage = "override-baseline";
                var existingOverrides = sdk.RetrieveMultiple(overrideQuery);
                if (existingOverrides.MoreRecords || existingOverrides.Entities.Count > 1)
                    throw new InvalidOperationException("P8 customization override baseline is ambiguous.");
                if (existingOverrides.Entities.Count == 1)
                {
                    var row = existingOverrides.Entities[0];
                    if (row.Id == Guid.Empty
                        || row.GetAttributeValue<EntityReference>("new_accountid")?.Id != church
                        || row.GetAttributeValue<string>("new_key") != "Fees.Notify.Template"
                        || row.GetAttributeValue<string>("new_culture") != "qps-ploc")
                        throw new InvalidOperationException("P8 customization override identity mismatch.");
                    originalCustomizationTemplateExists = true;
                    originalCustomizationTemplate = row.GetAttributeValue<string>("new_text")
                        ?? throw new InvalidOperationException("P8 customization override baseline text is missing.");
                }

                var contextA = new TextContext(church, "qps-ploc", "en-US",
                    OrganizationName, "LineWebhook");
                stage = "host-builder";
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    Args = Array.Empty<string>(),
                    ApplicationName = typeof(global::SpeechMessageProducts.LineWebhook.Program).Assembly.GetName().Name,
                    ContentRootPath = Path.Combine(root, "SpeechMessageProducts.LineWebhook"),
                    EnvironmentName = Environments.Development
                });
                stage = "host-secrets";
                builder.Configuration.AddUserSecrets("SpeechMessage.Dynamics.Deployment.P0");
                stage = "host-settings";
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CrmConnection:Organization"] = OrganizationName,
                    ["Localization:ChurchOverrides:Enabled"] = "true",
                    ["Localization:AllowPseudoLocale"] = "true",
                    ["Messaging:NotifyRelay:Enabled"] = "true",
                    ["ExceptionNotifications:SendLine"] = "false"
                });
                try
                {
                    stage = "host-build";
                    customizationApp = global::SpeechMessageProducts.LineWebhook.Program.BuildApplication(builder);
                }
                catch
                {
                    ((IDisposable)builder.Configuration).Dispose();
                    throw;
                }

                var cache = customizationApp.Services.GetRequiredService<ChurchTextOverrideCache>();
                var catalog = customizationApp.Services.GetRequiredService<SpeechMessage.Localization.ITextCatalog>();
                var contextB = new TextContext(customizationChurchB.Value, "qps-ploc", "en-US",
                    OrganizationName, "LineWebhook");
                stage = "cache-load";
                await cache.LoadAsync(contextA, ReadTextChangedOn(church));
                await cache.LoadAsync(contextB, ReadTextChangedOn(customizationChurchB.Value));

                customizationSettings = new ChurchSettingsService(new LiveChurchSettingsStore(sdk),
                    new LiveChurchSettingsCacheInvalidator(cache),
                    GoodShepherdTextCatalog.CreateOverrideValidator("xxxxxxxxxxxxxx"),
                    new LiveChurchSettingsClock());
                var customizationSavedAtUtc = DateTime.UtcNow;
                stage = "override-save";
                customizationOverrideMutationAttempted = true;
                await customizationSettings.SaveOverrideAsync(new AuthorizedChurch(church), contextA,
                    "Fees.Notify.Template", "qps-ploc", template);
                customizationCategoryMutationAttempted = true;
                stage = "category-add";
                var category = await customizationSettings.AddOfferingCategoryAsync(new AuthorizedChurch(church),
                    contextA, customizationCategoryName, 0, null, null);
                customizationCategoryId = category.Id;
                Save();
                await cache.LoadAsync(contextA, ReadTextChangedOn(church));
                if (DateTime.UtcNow - customizationSavedAtUtc > TimeSpan.FromSeconds(60))
                    throw new InvalidOperationException("P8 customization host composition exceeded 60 seconds.");

                var composedA = FeeNotificationRule.Compose(catalog, contextA, "A 測試會友",
                    new DateTime(2026, 11, 22, 4, 5, 6, DateTimeKind.Utc), "Asia/Taipei",
                    100000000, 100000000, "P8 A", 100m, category.Name);
                var composedB = FeeNotificationRule.Compose(catalog, contextB, "B 合成會友",
                    new DateTime(2026, 11, 22, 4, 5, 6, DateTimeKind.Utc), "Asia/Taipei",
                    100000000, 100000000, "B baseline", 100m);
                if (!composedA.Contains("P8 A自訂通知", StringComparison.Ordinal)
                    || !composedA.Contains(customizationCategoryName, StringComparison.Ordinal)
                    || composedB.Contains("P8 A自訂通知", StringComparison.Ordinal)
                    || composedB.Contains(customizationCategoryName, StringComparison.Ordinal))
                    throw new InvalidOperationException("P8 customization A/B composition isolation failed.");
                output.WriteLine("P8 A/B composition: A override and category applied; B baseline unchanged.");

                var feeRow = new Entity("new_fee", fee)
                {
                    ["new_name"] = marker,
                    ["new_contact_new_fee"] = new EntityReference("contact", contact),
                    ["new_pay_date"] = DateTime.UtcNow,
                    ["new_fee_really_paid"] = new Microsoft.Xrm.Sdk.Money(100),
                    ["new_pay_way"] = new OptionSetValue(100000000),
                    ["new_category"] = new OptionSetValue(100000000),
                    ["new_others"] = "P8 A"
                };
                feeRow["new_offeringcategoryid"] = new EntityReference("new_offeringcategory", category.Id);
                stage = "fee-create";
                feeAttempted = true;
                if (sdk.Create(feeRow) != fee)
                    throw new InvalidOperationException("P8 customization fee identity mismatch.");
                stage = "task-enqueue";
                task = MemberNotifyRelay.Enqueue(sdk, MemberNotifyRelayKind.FeeNotify, fee, 1, 0, instance);
                Save();
                if (!task.HasValue)
                    throw new InvalidOperationException("P8 customization relay did not enqueue.");

                stage = "competing-consumer-check";
                output.WriteLine("P8 customization relay queued; checking competing IIS consumer for 120 seconds.");
                for (var index = 0; index < 24; index++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    var current = ReadTask();
                    if (current.GetAttributeValue<OptionSetValue>("new_taskstatus")?.Value != 100000000
                        || current.GetAttributeValue<int>("new_attempts") != 0)
                        throw new InvalidOperationException("Unexpected competing outbound consumer detected.");
                }

                stage = "relay-send";
                var before = await UsageAsync();
                worker = customizationApp.Services.GetServices<IHostedService>()
                    .OfType<OutboundTaskWorker>().Single();
                await worker.StartAsync(CancellationToken.None);
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (true)
                {
                    var current = ReadTask();
                    var state = current.GetAttributeValue<OptionSetValue>("new_taskstatus")?.Value;
                    if (state == 100000002) break;
                    if (state == 100000003 || current.GetAttributeValue<int>("new_attempts") != 0
                        || DateTime.UtcNow >= deadline)
                        throw new InvalidOperationException("P8 customization relay completion failed.");
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }
                await worker.StopAsync(CancellationToken.None);
                worker = null;
                var after = await UsageAsync();
                if (after - before != 1)
                    throw new InvalidOperationException("P8 customization test Channel usage difference was not one.");
                output.WriteLine("P8 customization relay: Completed within 60 seconds; attempts=0; test Channel usage delta=1.");
            }
            catch (Exception exception)
            {
                output.WriteLine(FormatCustomizationFailure(stage, exception));
                throw new InvalidOperationException("P8 customization live acceptance failed; consult safe operation evidence.");
            }
            finally
            {
                await CleanupAsync();
            }
        }

        /// <summary>僅讀真實教會覆寫來源；不寫 Dataverse 或呼叫 LINE，失敗只輸出固定安全階段診斷。</summary>
        internal async Task ProbeCustomizationCacheReadOnlyAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ApplicationName = typeof(global::SpeechMessageProducts.LineWebhook.Program).Assembly.GetName().Name,
                ContentRootPath = Path.Combine(root, "SpeechMessageProducts.LineWebhook"),
                EnvironmentName = Environments.Development
            });
            builder.Configuration.AddUserSecrets("SpeechMessage.Dynamics.Deployment.P0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:Organization"] = OrganizationName,
                ["Localization:ChurchOverrides:Enabled"] = "true",
                ["Localization:AllowPseudoLocale"] = "true",
                ["Messaging:NotifyRelay:Enabled"] = "false",
                ["ExceptionNotifications:SendLine"] = "false"
            });
            WebApplication? app = null;
            try
            {
                app = global::SpeechMessageProducts.LineWebhook.Program.BuildApplication(builder);
                var cache = app.Services.GetRequiredService<ChurchTextOverrideCache>();
                var context = new TextContext(church, "qps-ploc", "en-US",
                    OrganizationName, "LineWebhook");
                await cache.LoadAsync(context, ReadTextChangedOn(church));
                output.WriteLine("Read-only override snapshot load: completed.");
            }
            catch (Exception exception)
            {
                output.WriteLine(FormatCustomizationFailure("read-only-cache-load", exception));
                throw new InvalidOperationException("Read-only customization snapshot load failed.");
            }
            finally
            {
                if (app is not null)
                    await app.DisposeAsync();
                else
                    ((IDisposable)builder.Configuration).Dispose();
            }
        }

        /// <summary>套用本案例 qps-ploc 租約，並以 D365 讀回確認；finally 會恢復原偏好。</summary>
        private void SetPseudoContactLanguage()
        {
            var current = sdk.Retrieve("contact", contact, new ColumnSet("new_preferredlanguage"));
            if (current.Id != contact || current.LogicalName != "contact")
                throw new InvalidOperationException("P8 pseudo contact identity rejected.");
            originalPreferredLanguage = current.GetAttributeValue<string>("new_preferredlanguage");
            preferredLanguageAttempted = true;
            sdk.Update(new Entity("contact", contact) { ["new_preferredlanguage"] = "qps-ploc" });
            var changed = sdk.Retrieve("contact", contact, new ColumnSet("new_preferredlanguage"));
            if (!string.Equals(changed.GetAttributeValue<string>("new_preferredlanguage"), "qps-ploc", StringComparison.Ordinal))
                throw new InvalidOperationException("P8 pseudo language lease was not applied.");
        }

        /// <summary>只讀指定 account 的 UTC 改字版本；不以本機時間取代來源版本。</summary>
        private DateTime ReadTextChangedOn(Guid churchId)
        {
            var account = sdk.Retrieve("account", churchId, new ColumnSet("new_textchangedon"));
            if (account.Id != churchId || account.LogicalName != "account"
                || account.GetAttributeValue<DateTime?>("new_textchangedon") is not { } changedOn)
                throw new InvalidOperationException("P8 text change timestamp is unavailable.");
            return changedOn.Kind == DateTimeKind.Utc
                ? changedOn
                : DateTime.SpecifyKind(changedOn, DateTimeKind.Utc);
        }
        /// <summary>只為固定端點建立獨占SDK並設15秒transport上限；每次scope唯一釋放，不提前關閉未排空I/O。</summary>
        private IOrganizationService OpenSdk()
        {
            var client = new CrmConnectionService().CreateOnPremiseClient(Required("OrganizationUrl"), Required("Username"), Required("Password"));
            try
            {
                if(client is not PowerPlatform.Dataverse.Client.OnPremiseClient onPremise)
                    throw new InvalidOperationException("P7 bounded SDK type unavailable.");
                onPremise.Timeout=TimeSpan.FromSeconds(15);
                return client;
            }
            catch { (client as IDisposable)?.Dispose(); throw; }
        }
        /// <summary>D34恰一測試綁定、來源教會相符、全部頻道加密token均為測試token才可寫入。</summary>
        private void VerifySafety()
        {
            if (sdk.Execute(new RetrieveCurrentOrganizationRequest()) is not RetrieveCurrentOrganizationResponse who
                || who.Detail?.UniqueName != "goodshepherdback")
                throw new InvalidOperationException("P7 server rejected.");
            if (sdk.Execute(new WhoAmIRequest()) is not WhoAmIResponse identity
                || identity.OrganizationId == Guid.Empty)
                throw new InvalidOperationException("P7 organization identity unavailable.");
            organizationId = identity.OrganizationId;
            var query = new QueryExpression("contact") { TopCount = 2, ColumnSet = new ColumnSet("parentcustomerid", "new_lineid") }; query.Criteria.AddCondition("new_lineid", ConditionOperator.NotNull); query.Criteria.AddCondition("new_lineid", ConditionOperator.NotEqual, "");
            var rows = sdk.RetrieveMultiple(query); if (rows.MoreRecords || rows.Entities.Count != 1 || rows.Entities[0].Id != contact || rows.Entities[0].GetAttributeValue<string>("new_lineid") != Required("TestLineUserId") || rows.Entities[0].GetAttributeValue<EntityReference>("parentcustomerid")?.Id != church) throw new InvalidOperationException("P7 D34 contact rejected.");
            var channels = sdk.RetrieveMultiple(new QueryExpression("new_linechannel") { TopCount = 101, ColumnSet = new ColumnSet("new_accesstokenencrypted") });
            if (channels.MoreRecords || channels.Entities.Count is < 1 or > 100) throw new InvalidOperationException("P7 D34 Channel inventory rejected.");
            foreach (var channel in channels.Entities) if (ChannelTokenProtector.Unprotect(channel.GetAttributeValue<string>("new_accesstokenencrypted"), key) != Required("TestChannelToken")) throw new InvalidOperationException("P7 D34 Channel rejected.");
            // 同時拒絕啟用中的新外殼及所有清單列出的舊型別，避免Create fee觸發舊程式。
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(root, "d365", "registration", "plugins.registration.json")));
            var types = ReadClrTypes(manifest);
            var steps = new QueryExpression("sdkmessageprocessingstep") { TopCount = 1, ColumnSet = new ColumnSet(false) };
            steps.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var plugin = steps.AddLink("plugintype", "eventhandler", "plugintypeid");
            var typeFilter = new ConditionExpression("typename", ConditionOperator.In);
            foreach (var type in types) typeFilter.Values.Add(type);
            plugin.LinkCriteria.AddCondition(typeFilter);
            if (sdk.RetrieveMultiple(steps).Entities.Count != 0) throw new InvalidOperationException("P7 requires all managed and legacy steps disabled.");
            var flows = new QueryExpression("workflow") { TopCount = 1001, ColumnSet = new ColumnSet("xaml") };
            flows.Criteria.AddCondition("statecode", ConditionOperator.Equal, 1);
            var activeFlows = sdk.RetrieveMultiple(flows);
            if (activeFlows.MoreRecords || activeFlows.Entities.Count > 1000 || activeFlows.Entities.Any(flow => types.Any(type => ((flow.GetAttributeValue<string>("xaml") ?? "").Contains(type, StringComparison.Ordinal) || (flow.GetAttributeValue<string>("xaml") ?? "").Contains(string.Concat("clr-namespace:".AsSpan(), type.AsSpan(0, type.LastIndexOf('.'))), StringComparison.Ordinal)))))
                throw new InvalidOperationException("P7 requires legacy activity workflows disabled.");
            output.WriteLine("D34 passed: one test binding; all Channels match test token.");
        }
        /// <summary>只讀本次待辦正式ID，狀態及重試計數，不讀或輸出payload／秘密。</summary>
        private Entity ReadTask() => sdk.Retrieve("new_outboundtask", task!.Value, new ColumnSet("new_taskstatus", "new_attempts"));
        /// <summary>只對固定api.line.me讀測試Channel用量，每次request自有Authorization，無共用header。</summary>
        private async Task<long> UsageAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.line.me/v2/bot/message/quota/consumption"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Required("TestChannelToken"));
            using var response = await http.SendAsync(request); if (!response.IsSuccessStatusCode) throw new InvalidOperationException("P7 test Channel usage unavailable.");
            var text = await response.Content.ReadAsStringAsync(); if (text.Length > 4096) throw new InvalidOperationException("P7 usage response bound exceeded.");
            var value = JObject.Parse(text)["totalUsage"]; return value?.Type == JTokenType.Integer ? (long)value : throw new InvalidOperationException("P7 usage response invalid.");
        }
        /// <summary>只有必要鍵名可出現在錯誤，值不記錄、不回落其他profile。</summary>
        private string Required(string name) => configuration[Prefix + name] is { Length: > 0 } value ? value : throw new InvalidOperationException("P7 required test setting missing.");
        /// <summary>停止並排空worker後按精確正式ID及本次marker核對／刪除；未知來源不得刪。</summary>
        private async Task CleanupAsync()
        {
            if (cleanupAttempted) return;
            cleanupAttempted = true;
            try
            {
                await CleanupCoreAsync();
            }
            finally
            {
                try
                {
                    await RestorePreferredLanguageAsync();
                }
                finally
                {
                    if (customizationApp is not null)
                    {
                        await customizationApp.DisposeAsync();
                        customizationApp = null;
                    }
                }
            }
        }

        /// <summary>執行共用測試資料與worker清理；語系回復由外層finally獨立擁有。</summary>
        private async Task CleanupCoreAsync()
        {
            if (worker is not null) { await worker.StopAsync(CancellationToken.None); worker = null; }
            if (provider is not null) { await provider.DisposeAsync(); provider = null; }
            // 設定是worker與scoped handler的借用依賴；只有停止及scope釋放成功後才能關閉。
            if (hostConfiguration is not null) { ((IDisposable)hostConfiguration).Dispose(); hostConfiguration = null; }
            var independentCleanupFailures = new List<Exception>();
            await TryCleanupAsync(CleanupCustomizationAsync, independentCleanupFailures);
            if (feeAttempted && !task.HasValue)
            {
                // Create回應中斷時按寫前已保存的完整事件鍵回查，不能因未取得回傳ID遺留待辦。
                var recovery = new QueryExpression("new_outboundtask") { TopCount = 2, ColumnSet = new ColumnSet(false) };
                recovery.Criteria.AddCondition("new_idempotencykey", ConditionOperator.Equal, idempotencyKey);
                var recovered = sdk.RetrieveMultiple(recovery);
                if (recovered.MoreRecords || recovered.Entities.Count > 1) throw new InvalidOperationException("P7 cleanup task recovery conflict.");
                if (recovered.Entities.Count == 1) task = recovered.Entities[0].Id;
            }
            if (task.HasValue)
            {
                var q = new QueryExpression("new_outboundtask") { TopCount = 2, ColumnSet = new ColumnSet("new_accountid", "new_payload") }; q.Criteria.AddCondition("new_outboundtaskid", ConditionOperator.Equal, task.Value);
                var rows = sdk.RetrieveMultiple(q); if (rows.MoreRecords || rows.Entities.Count > 1) throw new InvalidOperationException("P7 cleanup identity conflict.");
                if (rows.Entities.Count == 1) { var row = rows.Entities[0]; if (row.GetAttributeValue<EntityReference>("new_accountid")?.Id != church || !OwnsRecord(JObject.Parse(row.GetAttributeValue<string>("new_payload")), fee)) throw new InvalidOperationException("P7 cleanup ownership conflict."); sdk.Delete("new_outboundtask", task.Value); }
            }
            if (feeAttempted)
            {
                var q = new QueryExpression("new_fee") { TopCount = 2, ColumnSet = new ColumnSet("new_name") }; q.Criteria.AddCondition("new_feeid", ConditionOperator.Equal, fee);
                var rows = sdk.RetrieveMultiple(q); if (rows.MoreRecords || rows.Entities.Count > 1) throw new InvalidOperationException("P7 cleanup identity conflict.");
                if (rows.Entities.Count == 1) { if (rows.Entities[0].GetAttributeValue<string>("new_name") != marker) throw new InvalidOperationException("P7 cleanup marker conflict."); sdk.Delete("new_fee", fee); }
            }
            foreach (var pair in new[] { ("new_fee", "new_feeid", fee), ("new_outboundtask", "new_outboundtaskid", task ?? Guid.Empty) })
            {
                if (pair.Item3 == Guid.Empty) continue; var q = new QueryExpression(pair.Item1) { TopCount = 1, ColumnSet = new ColumnSet(false) }; q.Criteria.AddCondition(pair.Item2, ConditionOperator.Equal, pair.Item3);
                if (sdk.RetrieveMultiple(q).Entities.Count != 0) throw new InvalidOperationException("P7 cleanup residue detected.");
            }
            if (independentCleanupFailures.Count != 0)
                throw new InvalidOperationException("P8 customization cleanup incomplete; independent relay cleanup was attempted.");
            output.WriteLine("Cleanup: test fees=0; relay tasks=0; customization override restored; category inactive; temporary church=0; worker drained; hosts disposed.");
        }

        /// <summary>
        /// 回復教會 A 的原覆寫、停用本次類別並刪除本次 B 教會。各項獨立嘗試並於結尾
        /// 只以固定診斷表示不完整，避免一筆故障跳過其他資料清理；每次寫入前仍核對精確
        /// 教會範圍、正式 ID 與本次唯一標記，未知或衝突資料一律不刪不改。
        /// </summary>
        private async Task CleanupCustomizationAsync()
        {
            List<Exception>? failures = null;
            if (customizationOverrideMutationAttempted && customizationSettings is not null)
            {
                try
                {
                    var context = new TextContext(church, "qps-ploc", "en-US",
                        OrganizationName, "LineWebhook");
                    if (originalCustomizationTemplateExists)
                    {
                        await customizationSettings.SaveOverrideAsync(new AuthorizedChurch(church), context,
                            "Fees.Notify.Template", "qps-ploc", originalCustomizationTemplate!);
                    }
                    else
                    {
                        await customizationSettings.ResetOverrideAsync(new AuthorizedChurch(church), context,
                            "Fees.Notify.Template", "qps-ploc");
                    }

                    var query = new QueryExpression("new_localizedtext")
                    {
                        TopCount = 2,
                        ColumnSet = new ColumnSet("new_accountid", "new_key", "new_culture", "new_text")
                    };
                    query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, church);
                    query.Criteria.AddCondition("new_key", ConditionOperator.Equal, "Fees.Notify.Template");
                    query.Criteria.AddCondition("new_culture", ConditionOperator.Equal, "qps-ploc");
                    var rows = sdk.RetrieveMultiple(query);
                    if (rows.MoreRecords || rows.Entities.Count != (originalCustomizationTemplateExists ? 1 : 0))
                        throw new InvalidOperationException("P8 override cleanup readback failed.");
                    if (originalCustomizationTemplateExists)
                    {
                        var restored = rows.Entities[0];
                        if (restored.Id == Guid.Empty
                            || restored.GetAttributeValue<EntityReference>("new_accountid")?.Id != church
                            || restored.GetAttributeValue<string>("new_text") != originalCustomizationTemplate)
                            throw new InvalidOperationException("P8 override cleanup ownership or value mismatch.");
                    }
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            if (customizationCategoryMutationAttempted)
            {
                try
                {
                    var categoryId = customizationCategoryId ?? FindOwnedCustomizationCategoryId();
                    if (categoryId.HasValue)
                    {
                        var category = ReadOwnedCustomizationCategory(categoryId.Value);
                        if (category is null)
                            throw new InvalidOperationException("P8 customization category disappeared before cleanup.");
                        if (category.IsActive)
                        {
                            try
                            {
                                await RequireCustomizationSettings().UpdateOfferingCategoryAsync(
                                    new AuthorizedChurch(church),
                                    new TextContext(church, "qps-ploc", "en-US",
                                        OrganizationName, "LineWebhook"),
                                    category.Id, category.Name, category.SortOrder, category.AccountingCode,
                                    category.LegacyOptionValue, false);
                            }
                            catch
                            {
                                var afterUpdate = ReadOwnedCustomizationCategory(category.Id);
                                if (afterUpdate is null)
                                    throw new InvalidOperationException("P8 category state update lost its owned row.");
                                if (afterUpdate.IsActive)
                                {
                                    sdk.Execute(new SetStateRequest
                                    {
                                        EntityMoniker = new EntityReference("new_offeringcategory", category.Id),
                                        State = new OptionSetValue(1),
                                        Status = new OptionSetValue(2)
                                    });
                                }
                            }
                        }
                        var inactive = ReadOwnedCustomizationCategory(categoryId.Value);
                        if (inactive is null || inactive.IsActive)
                            throw new InvalidOperationException("P8 customization category inactive readback failed.");
                    }
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            if (customizationBCreateAttempted && customizationChurchB.HasValue)
            {
                try
                {
                    var query = new QueryExpression("account")
                    {
                        TopCount = 2,
                        ColumnSet = new ColumnSet("name")
                    };
                    query.Criteria.AddCondition("accountid", ConditionOperator.Equal, customizationChurchB.Value);
                    var rows = sdk.RetrieveMultiple(query);
                    if (rows.MoreRecords || rows.Entities.Count > 1)
                        throw new InvalidOperationException("P8 temporary church cleanup identity conflict.");
                    if (rows.Entities.Count == 1)
                    {
                        if (!string.Equals(rows.Entities[0].GetAttributeValue<string>("name"),
                                customizationChurchBName, StringComparison.Ordinal))
                            throw new InvalidOperationException("P8 temporary church cleanup ownership conflict.");
                        sdk.Delete("account", customizationChurchB.Value);
                    }
                    var residual = new QueryExpression("account") { TopCount = 1, ColumnSet = new ColumnSet(false) };
                    residual.Criteria.AddCondition("accountid", ConditionOperator.Equal, customizationChurchB.Value);
                    if (sdk.RetrieveMultiple(residual).Entities.Count != 0)
                        throw new InvalidOperationException("P8 temporary church residue detected.");
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            if (failures is not null)
                throw new InvalidOperationException("P8 customization cleanup incomplete.");
        }

        /// <summary>回傳已建立的設定服務；清理階段不能在原服務未成功初始化時猜測儲存 adapter。</summary>
        private ChurchSettingsService RequireCustomizationSettings() =>
            customizationSettings ?? throw new InvalidOperationException("P8 customization settings owner unavailable.");

        /// <summary>回應遺失時以本次唯一類別名稱及 A 教會 lookup 找回正式 ID，重複候選拒絕。</summary>
        private Guid? FindOwnedCustomizationCategoryId()
        {
            if (string.IsNullOrWhiteSpace(customizationCategoryName))
                throw new InvalidOperationException("P8 category recovery marker unavailable.");
            var query = new QueryExpression("new_offeringcategory")
            {
                TopCount = 2,
                ColumnSet = new ColumnSet("new_accountid", "new_code", "new_name")
            };
            query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, church);
            query.Criteria.AddCondition("new_name", ConditionOperator.Equal, customizationCategoryName);
            var rows = sdk.RetrieveMultiple(query);
            if (rows.MoreRecords || rows.Entities.Count > 1)
                throw new InvalidOperationException("P8 category recovery identity conflict.");
            if (rows.Entities.Count == 0) return null;
            var row = rows.Entities[0];
            var code = row.GetAttributeValue<string>("new_code");
            if (row.Id == Guid.Empty
                || row.GetAttributeValue<EntityReference>("new_accountid")?.Id != church
                || !string.Equals(row.GetAttributeValue<string>("new_name"), customizationCategoryName, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(code)
                || !code.StartsWith("custom-", StringComparison.Ordinal))
                throw new InvalidOperationException("P8 category recovery ownership mismatch.");
            customizationCategoryId = row.Id;
            return row.Id;
        }

        /// <summary>讀回本次類別並驗證正式 ID、教會外鍵及唯一名稱後才轉為領域純值。</summary>
        private OfferingCategory? ReadOwnedCustomizationCategory(Guid id)
        {
            var query = new QueryExpression("new_offeringcategory")
            {
                TopCount = 2,
                ColumnSet = new ColumnSet("new_accountid", "new_code", "new_name", "new_sortorder",
                    "new_accountingcode", "new_legacyoptionvalue", "statecode")
            };
            query.Criteria.AddCondition("new_offeringcategoryid", ConditionOperator.Equal, id);
            var rows = sdk.RetrieveMultiple(query);
            if (rows.MoreRecords || rows.Entities.Count > 1)
                throw new InvalidOperationException("P8 category cleanup identity conflict.");
            if (rows.Entities.Count == 0) return null;
            var row = rows.Entities[0];
            if (row.Id != id
                || row.GetAttributeValue<EntityReference>("new_accountid")?.Id != church
                || !string.Equals(row.GetAttributeValue<string>("new_name"), customizationCategoryName, StringComparison.Ordinal))
                throw new InvalidOperationException("P8 category cleanup ownership mismatch.");
            return new OfferingCategory(row.Id, church, row.GetAttributeValue<string>("new_code") ?? string.Empty,
                row.GetAttributeValue<string>("new_name") ?? string.Empty,
                row.GetAttributeValue<int?>("new_sortorder") ?? 0,
                row.GetAttributeValue<string>("new_accountingcode"),
                row.GetAttributeValue<int?>("new_legacyoptionvalue"),
                row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0);
        }

        /// <summary>不論前段資料清理成功與否都回復本次 qps-ploc 租約，避免測試偏好殘留。</summary>
        private Task RestorePreferredLanguageAsync()
        {
            if (!preferredLanguageAttempted) return Task.CompletedTask;
            sdk.Update(new Entity("contact", contact) { ["new_preferredlanguage"] = originalPreferredLanguage });
            var restored = sdk.Retrieve("contact", contact, new ColumnSet("new_preferredlanguage"));
            if (!string.Equals(restored.GetAttributeValue<string>("new_preferredlanguage"), originalPreferredLanguage, StringComparison.Ordinal))
                throw new InvalidOperationException("P8 pseudo language restore failed.");
            preferredLanguageAttempted = false;
            return Task.CompletedTask;
        }
        /// <summary>finally即使Run失敗仍重做精確清理，最後關閉所有資源及清key。</summary>
        public async ValueTask DisposeAsync()
        {
            try { await CleanupAsync(); }
            finally
            {
                try { (sdk as IDisposable)?.Dispose(); }
                finally
                {
                    Array.Clear(key);
                    try { http.Dispose(); }
                    finally { ((IDisposable)configuration).Dispose(); }
                }
            }
        }
        /// <summary>只沿本工作樹向上找方案根，證據不寫其他checkout。</summary>
        private static string FindRoot()
        {
            for (var p = new DirectoryInfo(System.AppContext.BaseDirectory); p is not null; p = p.Parent) if (File.Exists(Path.Combine(p.FullName, "SpeechMessageProducts.sln"))) return p.FullName;
            throw new InvalidOperationException("P7 worktree unavailable.");
        }

        /// <summary>
        /// 僅供本實機案例使用的 IChurchSettingsStore adapter；每次資料列操作都以
        /// church ID 限定，回傳值不含 SDK Entity。此 adapter 不擁有或釋放外部 SDK。
        /// </summary>
        private sealed class LiveChurchSettingsStore(IOrganizationService service) : IChurchSettingsStore
        {
            private const int MaximumCategories = 200;

            /// <summary>依教會、設定鍵與語系列建立 Dataverse 原子 Upsert，取消只在 SDK 前檢查。</summary>
            public Task UpsertOverrideAsync(ChurchTextOverrideWrite value, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(value);
                cancellationToken.ThrowIfCancellationRequested();
                var target = new Entity("new_localizedtext");
                target.KeyAttributes["new_accountid"] = new EntityReference("account", value.ChurchId);
                target.KeyAttributes["new_key"] = value.Key;
                target.KeyAttributes["new_culture"] = value.Culture;
                target["new_accountid"] = new EntityReference("account", value.ChurchId);
                target["new_key"] = value.Key;
                target["new_culture"] = value.Culture;
                target["new_name"] = value.Key + "|" + value.Culture;
                target["new_text"] = value.Text;
                if (service.Execute(new UpsertRequest { Target = target }) is not UpsertResponse)
                    throw new InvalidOperationException("P8 override upsert returned an unexpected response.");
                return Task.CompletedTask;
            }

            /// <summary>只刪除指定教會鍵／語系唯一列；身份衝突時 fail closed。</summary>
            public Task RemoveOverrideAsync(Guid churchId, string key, string culture, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new QueryExpression("new_localizedtext")
                {
                    TopCount = 2,
                    ColumnSet = new ColumnSet("new_accountid", "new_key", "new_culture")
                };
                query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, churchId);
                query.Criteria.AddCondition("new_key", ConditionOperator.Equal, key);
                query.Criteria.AddCondition("new_culture", ConditionOperator.Equal, culture);
                var rows = service.RetrieveMultiple(query);
                if (rows.MoreRecords || rows.Entities.Count > 1)
                    throw new InvalidOperationException("P8 override identity is not unique.");
                if (rows.Entities.Count == 0) return Task.CompletedTask;
                var row = rows.Entities[0];
                if (row.Id == Guid.Empty
                    || row.GetAttributeValue<EntityReference>("new_accountid")?.Id != churchId
                    || row.GetAttributeValue<string>("new_key") != key
                    || row.GetAttributeValue<string>("new_culture") != culture)
                    throw new InvalidOperationException("P8 override removal ownership mismatch.");
                service.Delete("new_localizedtext", row.Id);
                return Task.CompletedTask;
            }

            /// <summary>只把 UTC 版本寫回指定教會 account，來源值非 UTC 時拒絕。</summary>
            public Task TouchTextChangedOnAsync(Guid churchId, DateTime changedOnUtc, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (changedOnUtc.Kind != DateTimeKind.Utc)
                    throw new ArgumentException("UTC is required.", nameof(changedOnUtc));
                service.Update(new Entity("account", churchId) { ["new_textchangedon"] = changedOnUtc });
                return Task.CompletedTask;
            }

            /// <summary>只讀本教會最多 200 個類別，保留資料庫 ID 並拒絕超額候選。</summary>
            public Task<IReadOnlyList<OfferingCategory>> ReadCategoriesAsync(Guid churchId, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new QueryExpression("new_offeringcategory")
                {
                    TopCount = MaximumCategories + 1,
                    ColumnSet = new ColumnSet("new_accountid", "new_code", "new_name", "new_sortorder",
                        "new_accountingcode", "new_legacyoptionvalue", "statecode")
                };
                query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, churchId);
                query.AddOrder("new_sortorder", OrderType.Ascending);
                query.AddOrder("new_offeringcategoryid", OrderType.Ascending);
                var rows = service.RetrieveMultiple(query);
                if (rows.MoreRecords || rows.Entities.Count > MaximumCategories)
                    throw new InvalidOperationException("P8 category candidate exceeded its bounded read.");
                IReadOnlyList<OfferingCategory> result = rows.Entities.Select(row =>
                    new OfferingCategory(row.Id,
                        row.GetAttributeValue<EntityReference>("new_accountid")?.Id ?? Guid.Empty,
                        row.GetAttributeValue<string>("new_code") ?? string.Empty,
                        row.GetAttributeValue<string>("new_name") ?? string.Empty,
                        row.GetAttributeValue<int?>("new_sortorder") ?? 0,
                        row.GetAttributeValue<string>("new_accountingcode"),
                        row.GetAttributeValue<int?>("new_legacyoptionvalue"),
                        row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0)).ToArray();
                return Task.FromResult(result);
            }

            /// <summary>以教會 lookup 及精確代碼確認替代索引鍵候選是否已存在。</summary>
            public Task<bool> CategoryCodeExistsAsync(Guid churchId, string code, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new QueryExpression("new_offeringcategory")
                {
                    TopCount = 1,
                    ColumnSet = new ColumnSet("new_accountid", "new_code")
                };
                query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, churchId);
                query.Criteria.AddCondition("new_code", ConditionOperator.Equal, code);
                var rows = service.RetrieveMultiple(query);
                if (rows.Entities.Count == 0) return Task.FromResult(false);
                if (rows.Entities[0].GetAttributeValue<EntityReference>("new_accountid")?.Id != churchId
                    || !string.Equals(rows.Entities[0].GetAttributeValue<string>("new_code"), code, StringComparison.Ordinal))
                    throw new InvalidOperationException("P8 category code lookup ownership mismatch.");
                return Task.FromResult(true);
            }

            /// <summary>以教會加伺服器代碼 Upsert，並只接受回應中的正式 Target ID。</summary>
            public Task<Guid> AddCategoryAsync(Guid churchId, string code, string name, int sortOrder,
                string? accountingCode, int? legacyOptionValue, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = new Entity("new_offeringcategory");
                target.KeyAttributes["new_accountid"] = new EntityReference("account", churchId);
                target.KeyAttributes["new_code"] = code;
                target["new_accountid"] = new EntityReference("account", churchId);
                target["new_code"] = code;
                target["new_name"] = name;
                target["new_sortorder"] = sortOrder;
                if (accountingCode is not null) target["new_accountingcode"] = accountingCode;
                if (legacyOptionValue.HasValue) target["new_legacyoptionvalue"] = legacyOptionValue.Value;
                var response = service.Execute(new UpsertRequest { Target = target }) as UpsertResponse
                    ?? throw new InvalidOperationException("P8 category upsert returned an unexpected response.");
                var id = response.Target?.Id ?? Guid.Empty;
                return Task.FromResult(id != Guid.Empty
                    ? id
                    : throw new InvalidOperationException("P8 category upsert returned no stable Target ID."));
            }

            /// <summary>更新同教會已存在的正式類別；停用狀態先使用一般 Update，由 owner 負責讀回驗證。</summary>
            public Task UpdateCategoryAsync(Guid churchId, Guid id, string name, int sortOrder,
                string? accountingCode, int? legacyOptionValue, bool isActive, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new QueryExpression("new_offeringcategory")
                {
                    TopCount = 2,
                    ColumnSet = new ColumnSet("new_accountid")
                };
                query.Criteria.AddCondition("new_offeringcategoryid", ConditionOperator.Equal, id);
                var rows = service.RetrieveMultiple(query);
                if (rows.MoreRecords || rows.Entities.Count != 1
                    || rows.Entities[0].GetAttributeValue<EntityReference>("new_accountid")?.Id != churchId)
                    throw new InvalidOperationException("P8 category update ownership mismatch.");
                var target = new Entity("new_offeringcategory", id)
                {
                    ["new_accountid"] = new EntityReference("account", churchId),
                    ["new_name"] = name,
                    ["new_sortorder"] = sortOrder,
                    ["statecode"] = new OptionSetValue(isActive ? 0 : 1)
                };
                if (accountingCode is null) target.Attributes.Remove("new_accountingcode");
                else target["new_accountingcode"] = accountingCode;
                if (legacyOptionValue.HasValue) target["new_legacyoptionvalue"] = legacyOptionValue.Value;
                else target.Attributes.Remove("new_legacyoptionvalue");
                service.Update(target);
                return Task.CompletedTask;
            }

            /// <summary>此實機案例不讀稽核畫面；若誤用此介面則明確拒絕而非回傳假成功。</summary>
            public Task<IReadOnlyList<ChurchSettingsAuditRecord>> ReadAuditAsync(
                Guid churchId, int maximum, CancellationToken cancellationToken) =>
                throw new NotSupportedException("P8 live relay acceptance does not exercise the audit projection.");
        }

        /// <summary>把設定服務已持久化的單一 A scope 版本交給實際 LineWebhook cache owner。</summary>
        private sealed class LiveChurchSettingsCacheInvalidator(ChurchTextOverrideCache cache) : IChurchSettingsCacheInvalidator
        {
            /// <summary>拒絕未載入或不匹配的 scope，確保失效真正作用於此 Host 的 cache。</summary>
            public void Invalidate(TextContext context, DateTime changedOnUtc)
            {
                if (!cache.Invalidate(context, changedOnUtc))
                    throw new InvalidOperationException("P8 host cache rejected the customization invalidation.");
            }
        }

        /// <summary>提供 UTC 時刻且不建立 timer 或共享可變狀態。</summary>
        private sealed class LiveChurchSettingsClock : IChurchSettingsClock
        {
            /// <summary>每次讀取取得新的 UTC 值，不保留跨操作的時間狀態。</summary>
            public DateTime UtcNow => DateTime.UtcNow;
        }
    }
    /// <summary>實機只啟動代送worker，不接HTTP事件，任何去重呼叫拒絕。</summary>
    private sealed class NoDedupe : IWebhookEventDeduplicator
    {
        /// <summary>不提供假的正式去重能力，代送不應進入此入口。</summary>
        public ValueTask<IWebhookEventRegistration?> TryRegisterAsync(WebhookEnvelope envelope, CancellationToken ct) => throw new NotSupportedException();
    }
    /// <summary>實機只啟動代送worker，不註冊入站queue背景服務。</summary>
    private sealed class NoQueue : IWebhookEventQueue
    {
        /// <summary>不接收任何Webhook事件，意外呼叫明確拒絕。</summary>
        public ValueTask<bool> TryEnqueueAsync(WebhookEnvelope envelope, IWebhookEventRegistration registration, CancellationToken ct) => throw new NotSupportedException();
    }
}
