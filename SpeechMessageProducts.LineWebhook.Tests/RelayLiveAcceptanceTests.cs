/*
 * 檔案整體說明
 * 目的與定位：P7唯一測試組織代送實機驗收，Category=P7Live由一般runsettings排除。
 * 主要流程：只讀Deployment User Secrets，先核固定HTTPS服務與server uniqueName，再驗D34唯一
 * 測試綁定及全Channel均為測試token；確認沒有其他待辦，建立本次SMV-TEST-fee與第四類待辦。
 * 先等待兩分鐘確認測試IIS未認領，再啟動本機正式註冊的唯一代送worker，60秒內完成、用量+1。
 * 使用邊界與維護：不啟動HTTP／付款／QR，不改IIS／Node，不讀正式組織。資料寫入前把候選
 * 正式ID與測試marker落ignored ownership檔，失敗亦先停止worker排空再依精確ID清理及核零。
 * 資源與隔離：fixture擁有控制SDK、DI容器、設定根、HTTP client與key；每筆worker使用新scope SDK，
 * 金鑰只從User Secrets進記憶體，finally清零且不輸出任何憑證或回應本文；查詢有界，未知值拒絕。
 * 主機設定由fixture持有至worker停止及DI scope釋放之後，例外退出不能提前關閉在用設定。
 * 驗證證據只輸出筆數、狀態、用量差與零殘留；實機執行需明確Category篩選，離線不冒稱成功。
 */
using System.Net.Http.Headers;
using System.Text;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.Core.Security;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Hosting.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
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
    /// <summary>只驗正式來源ID，不以文字標記替代資料列身分；未知或畸形值拒絕刪除。</summary>
    private static bool OwnsRecord(JObject payload, Guid id) =>
        payload["recordId"] is { Type: JTokenType.String } token &&
        Guid.TryParseExact(token.Value<string>(), "D", out var actual) && actual != Guid.Empty && actual == id;
    /// <summary>只接真CLR完整名稱，不把image kind等同名JSON欄位拿去算命名空間；集合只活當次安全盤點。</summary>
    private static string[] ReadClrTypes(JObject manifest)=>manifest.SelectTokens("$..type").Values<string>()
        .Where(value=>!string.IsNullOrWhiteSpace(value)&&value.IndexOf('.',StringComparison.Ordinal)>0).Select(value=>value!).Distinct(StringComparer.Ordinal).ToArray();
    /// <summary>唯一操作owner；所有秘密只在記憶體與既有User Secrets，不進命令列或證據。</summary>
    private sealed class LiveOwner : IAsyncDisposable
    {
        private const string Prefix = "Deployment:Organizations:goodshepherdback:";
        private readonly ITestOutputHelper output;
        private readonly IConfigurationRoot configuration;
        private readonly byte[] key;
        private readonly IOrganizationService sdk;
        private readonly Guid church, contact, fee = Guid.NewGuid(), instance = Guid.NewGuid();
        private readonly string idempotencyKey;
        private Guid? task;
        private readonly string marker = "SMV-TEST-P7-Relay-" + Guid.NewGuid().ToString("N");
        private ServiceProvider? provider; private OutboundTaskWorker? worker;
        private IConfigurationRoot? hostConfiguration;
        private readonly HttpClient http = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        private readonly string root;
        private bool feeAttempted;
        /// <summary>建構只讀固定User Secrets與端點，失敗立即釋放已建立owner；不在錯誤回顯值。</summary>
        internal LiveOwner(ITestOutputHelper output)
        {
            this.output = output;
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
            catch { (sdk as IDisposable)?.Dispose(); Array.Clear(key); http.Dispose(); ((IDisposable)configuration).Dispose(); throw new InvalidOperationException("P7 live setup failed; sensitive details suppressed."); }
        }
        /// <summary>無其他工作與D34安全才寫測試資料，所有候選識別在Create前已落ownership。</summary>
        internal async Task RunAsync()
        {
            var directory = Path.Combine(root, "artifacts", "p7", "relay-live-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)); Directory.CreateDirectory(directory);
            var ownership = Path.Combine(directory, "ownership.json");
            void Save() => File.WriteAllText(ownership, new JObject { ["feeId"] = fee, ["taskId"] = task, ["marker"] = marker, ["idempotencyKey"] = idempotencyKey }.ToString(), new UTF8Encoding(false));
            Save();
            try
            {
                var pending = sdk.RetrieveMultiple(new QueryExpression("new_outboundtask") { TopCount = 2, ColumnSet = new ColumnSet(false) });
                if (pending.MoreRecords || pending.Entities.Count != 0) throw new InvalidOperationException("P7 requires empty outbound baseline.");
                var row = new Entity("new_fee", fee) { ["new_name"] = marker, ["new_contact_new_fee"] = new EntityReference("contact", contact), ["new_pay_date"] = DateTime.UtcNow, ["new_fee_really_paid"] = new Money(100), ["new_pay_way"] = new OptionSetValue(100000000) };
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
                services.AddLineWebhook(hostConfig); provider = services.BuildServiceProvider(validateScopes: true);
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
                output.WriteLine("P7 live failure type={0}; details suppressed.", exception.GetType().Name);
                throw new InvalidOperationException("P7 relay live acceptance failed; consult safe operation evidence.");
            }
            finally { await CleanupAsync(); }
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
            if (sdk.Execute(new RetrieveCurrentOrganizationRequest()) is not RetrieveCurrentOrganizationResponse who || who.Detail?.UniqueName != "goodshepherdback") throw new InvalidOperationException("P7 server rejected.");
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
            if (worker is not null) { await worker.StopAsync(CancellationToken.None); worker = null; }
            if (provider is not null) { await provider.DisposeAsync(); provider = null; }
            // 設定是worker與scoped handler的借用依賴；只有停止及scope釋放成功後才能關閉。
            if (hostConfiguration is not null) { ((IDisposable)hostConfiguration).Dispose(); hostConfiguration = null; }
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
            output.WriteLine("Cleanup: test fees=0; relay tasks=0; worker stopped and scoped resources disposed.");
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
