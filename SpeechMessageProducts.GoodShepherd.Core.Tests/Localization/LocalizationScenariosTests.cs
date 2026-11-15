/*
 * 檔案整體說明
 *
 * 目的與定位：以 05A §4.2 的 localization 正式合成情境及人工期望檔，保護 P5-04
 * Core 設定規則與會友換綁服務；同一 Fact 在 net48 與 net10.0 執行，不複製產品規則。
 * 主要功能與流程：嚴格載入兩個嵌入 JSON，核對模組、new 基準及案例一對一，再把
 * trigger／seed 純值交給 LocaleChangeRules、OutboundTaskWriter 或 MemberRichMenuService。
 * outputs 核對 Core 真實輸出，writes 保留共用外殼完整意圖；Writer 擷取命令只核對其
 * 擁有的待辦欄位，資料庫預設欄位與文字時間更新由外殼測試真正執行。文字兩案在此
 * 僅驗效應計畫結構，不重寫外殼規則。固定 sourceId 不由待測函式重生期望。
 * 使用邊界與維護：來源替身只投影合成資料，不實作授權、語系選擇或 alias 規則；
 * Host 授權在本離線測試之外。每案獨占來源、JSON 與回呼集合，沒有跨案快取、
 * Session、金鑰、背景工作或外部連線。每檔最多一 MiB，stream／reader 由 using
 * 逆序釋放；HTTP owner 等待服務完成才 Dispose，handler 遇任何傳輸立即失敗。
 * Shadow 使用無效密文證明不解密，不宣稱 Live、Plugin 寫入或 D365 實機驗收。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.LineRecipients;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Localization;

/// <summary>
/// 以固定正式資源驅動真實 Core 規則與服務；所有合成依賴及輸出僅存活於一次測試。
/// Core 輸出逐欄比較，完整外殼意圖仍交外殼測試驗收，避免把純規則等同 SDK 執行。
/// </summary>
public sealed class LocalizationScenariosTests
{
    /// <summary>
    /// 固定六案共用外殼期望：文字兩案驗效應計畫，教會兩案跑規則與 Writer，
    /// 會友兩案跑服務。錯誤分開記錄 Core 型別與外殼轉換後型別，服務來源計數
    /// 只代表 Core 呼叫，不把外殼額外讀取算成 Core 契約。任何 HTTP 嘗試立即失敗。
    /// </summary>
    [Fact]
    public async Task Localization_Scenarios_MatchExpected()
    {
        var scenarios = ReadResource("scenarios");
        var expected = ReadResource("expected");
        Assert.Equal("localization", (string?)scenarios["module"]);
        Assert.Equal("localization", (string?)expected["module"]);
        Assert.Equal("new", (string?)expected["basis"]);
        Assert.Empty(Assert.IsType<JArray>(expected["declaredDifferences"]));
        var inputs = Assert.IsType<JArray>(scenarios["cases"]).Cast<JObject>().ToArray();
        var results = Assert.IsType<JArray>(expected["cases"]).Cast<JObject>().ToArray();
        Assert.Equal(6, inputs.Length);
        Assert.Equal("text-create|text-reassigned|church-valid-post|church-unreleased-error|member-bound|member-unbound",
            string.Join("|", inputs.Select(item => Text(item, "name"))));
        Assert.Equal(inputs.Length, results.Length);
        Assert.Equal(inputs.Length, inputs.Select(item => Text(item, "name")).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(results.Length, results.Select(item => Text(item, "name")).Distinct(StringComparer.Ordinal).Count());

        foreach (var scenario in inputs)
        {
            var name = Text(scenario, "name");
            var desired = Assert.Single(results, item => Text(item, "name") == name);
            var trigger = Assert.IsType<JObject>(scenario["trigger"]);
            var entrypoint = Text(trigger, "type");
            Assert.Equal(entrypoint, Text(desired, "entrypoint"));
            var outputs = new JObject();
            var rejected = false;
            try
            {
                switch (entrypoint)
                {
                    case "SpeechMessage.Dynamics.Plugins.Localization.LocalizedTextChangedPlugin":
                        ValidateTextPlan(scenario, desired);
                        break;
                    case "SpeechMessage.Dynamics.Plugins.Localization.ChurchLocaleChangedPlugin":
                        RunLocale(scenario, desired, outputs);
                        break;
                    case "SpeechMessage.Dynamics.Plugins.Localization.MemberLanguageChangedPlugin":
                        Assert.Empty(Assert.IsType<JArray>(desired["writes"]));
                        await RunMemberAsync(scenario, outputs);
                        break;
                    default:
                        throw new InvalidDataException("UnsupportedLocalizationEntrypoint");
                }
            }
            catch (BusinessRuleException error)
            {
                rejected = true;
                outputs["errorKey"] = error.TextKey.Value;
                outputs["coreException"] = error.GetType().FullName;
                Assert.Equal(error.TextKey.Value, error.Message);
                Assert.Empty(error.Arguments);
                Assert.Null(error.InnerException);
                Assert.Empty(Assert.IsType<JArray>(desired["writes"]));
                Assert.Equal("Microsoft.Xrm.Sdk.InvalidPluginExecutionException", (string?)desired["exception"]);
            }

            if (!rejected) Assert.Equal(JTokenType.Null, desired["exception"]!.Type);
            Assert.Empty(Assert.IsType<JArray>(desired["lineCalls"]));
            Assert.True(JToken.DeepEquals(desired["outputs"], outputs), "Localization core output mismatch: " + name);
        }
    }

    /// <summary>
    /// 文字失效邏輯目前只存在於 Plugin，Core 不能引用沙箱或複製其前後教會與整秒算法。
    /// 本方法只確認共用計畫非空、每筆指向唯一合成 account 且欄位為 UTC 整秒；
    /// 是否更新正確教會、順序及從 now 向下取整，由外殼讀同檔真正執行與比對。
    /// </summary>
    private static void ValidateTextPlan(JObject scenario, JObject desired)
    {
        var writes = Assert.IsType<JArray>(desired["writes"]);
        Assert.NotEmpty(writes);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var write in writes)
        {
            Assert.Equal("Update", Text(write, "operation"));
            Assert.Equal("account", Text(write, "entity"));
            var alias = Text(write, "alias");
            Assert.True(aliases.Add(alias));
            Assert.Single(Assert.IsType<JArray>(scenario["seed"]),
                seed => Text(seed, "alias") == alias && Text(seed, "entity") == "account");
            var fields = Assert.IsType<JObject>(write["fields"]);
            Assert.Single(fields.Properties());
            var timestamp = Text(fields, "new_textchangedon");
            Assert.EndsWith("Z", timestamp, StringComparison.Ordinal);
            Assert.Equal(0L, DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture).Ticks % TimeSpan.TicksPerSecond);
        }
    }

    /// <summary>
    /// 把正式 trigger 四欄送入產品規則；只序列化產品回傳值及來源識別，不在測試重寫
    /// 發布清單、排序、國碼或雜湊規則。成功後真正呼叫 Writer 並擷取純值命令，
    /// 核對共用期望的 payload、類型、教會及冪等鍵；SDK 寫入與狀態預設由外殼驗證。
    /// </summary>
    /// <param name="scenario">當案獨占的正式情境樹。</param>
    /// <param name="desired">與外殼共用的完整期望，只讀取 Writer 擁有的欄位。</param>
    /// <param name="outputs">當案輸出候選，完成後與人工期望完整比對。</param>
    private static void RunLocale(JObject scenario, JObject desired, JObject outputs)
    {
        var fields = Assert.IsType<JObject>(scenario["trigger"]!["fields"]);
        var settings = LocaleChangeRules.Validate(Text(fields, "new_defaultlanguage"),
            Text(fields, "new_enabledlanguages"), Text(fields, "new_timezone"), Text(fields, "new_countrycode"));
        outputs["defaultLanguage"] = settings.DefaultLanguage;
        outputs["enabledLanguages"] = settings.EnabledLanguages;
        outputs["timeZone"] = settings.TimeZone;
        outputs["countryCode"] = settings.CountryCode;
        var church = Identity(Text(scenario["church"]!, "alias"));
        var sourceId = LocaleChangeRules.SourceId(church, settings);
        outputs["sourceId"] = sourceId.ToString("D");
        var store = new CapturingStore();
        new OutboundTaskWriter(store).Enqueue(OutboundTaskType.RichMenuGenerate, church, null, sourceId,
            new Dictionary<string, object> { ["settings"] = sourceId }, CancellationToken.None);
        var command = Assert.IsType<OutboundTaskCommand>(store.Command);
        Assert.Equal(1, store.Calls);
        var write = Assert.Single(Assert.IsType<JArray>(desired["writes"]));
        Assert.Equal("Create", Text(write, "operation"));
        Assert.Equal("new_outboundtask", Text(write, "entity"));
        var planned = Assert.IsType<JObject>(write["fields"]);
        Assert.Equal(command.IdempotencyKey, Text(planned, "new_idempotencykey"));
        Assert.Equal(command.PayloadJson, Text(planned, "new_payload"));
        Assert.Equal((int)command.Type, (int)planned["new_type"]!["option"]!);
        Assert.Equal(command.ChurchId, Identity(Text(planned["new_accountid"]!, "ref")));
        Assert.Null(command.ContactId);
    }

    /// <summary>
    /// 借用當案來源並接線真正 resolver、alias 與換綁服務；無效密文只供證明 Shadow
    /// 不解密。回呼僅投影正式 Contact 的合成代號；HTTP 一律拒絕，不保存 request。
    /// finally 在預期業務拒絕時仍記讀取次數，client 在所有工作排空後釋放 handler。
    /// </summary>
    /// <param name="scenario">含當案教會、會友與頻道 seed 的正式輸入。</param>
    /// <param name="outputs">只保留當案安全 alias、拒絕分類及讀取計數。</param>
    private static async Task RunMemberAsync(JObject scenario, JObject outputs)
    {
        var source = new ScenarioSource(scenario);
        using var handler = new RejectHttpHandler();
        using var http = new HttpClient(handler, disposeHandler: false);
        var service = new MemberRichMenuService(new LinePushClient(http),
            new MemberCultureResolver(new LocalizationOptions()));
        var church = scenario["church"]!;
        var context = new TextContext(Identity(Text(church, "alias")), Text(church, "culture"),
            Text(church, "culture"), Text(church, "organizationName"), "GoodShepherd");
        try
        {
            var result = await service.LinkAsync(source, context, Identity("contact-1"),
                Text(source.ChurchFields, "accountnumber"), "main", "home", null,
                (contact, alias) =>
                {
                    Assert.Equal(Identity("contact-1"), contact);
                    Assert.Null(outputs["alias"]);
                    outputs["alias"] = alias;
                    outputs["contact"] = "contact-1";
                }, CancellationToken.None);
            outputs["success"] = result.Success;
            outputs["reason"] = result.Reason;
        }
        finally
        {
            outputs["memberReads"] = source.MemberReads;
            outputs["channelReads"] = source.ChannelReads;
            Assert.Equal(0, handler.Calls);
            Assert.Null(http.DefaultRequestHeaders.Authorization);
        }
    }

    /// <summary>
    /// 只從組件固定資源讀取，每檔上限一 MiB／三十二層，拒重複屬性及尾隨 JSON。
    /// 嚴格 UTF-8 且保留日期原文與 decimal；所有 reader／stream 在離開本方法時關閉。
    /// </summary>
    /// <param name="file">測試固定的 scenarios 或 expected 檔名，不接受外部路徑。</param>
    /// <returns>僅由本次測試擁有、不連結 stream 的 JSON 樹。</returns>
    private static JObject ReadResource(string file)
    {
        using var stream = typeof(LocalizationScenariosTests).Assembly.GetManifestResourceStream(
            "SpeechMessageProducts.GoodShepherd.Core.Tests.Golden.Legacy.localization." + file + ".json");
        Assert.NotNull(stream);
        Assert.InRange(stream!.Length, 1L, 1_048_576L);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        using var json = new JsonTextReader(reader)
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
            MaxDepth = 32,
        };
        var document = JObject.Load(json,
            new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        Assert.False(json.Read());
        return document;
    }

    /// <summary>只解碼必填 JSON 字串，不修剪或正規化；所有產品驗證仍交由待測規則執行。</summary>
    private static string Text(JToken value, string property) => Assert.IsType<JValue>(value[property]).Value as string
        ?? throw new InvalidDataException("LocalizationTextMissing");

    /// <summary>明列離線別名與固定合成主鍵，不根據顯示名稱或未知輸入猜測資料列身分。</summary>
    private static Guid Identity(string alias) => alias switch
    {
        "church-a" => new Guid("11111111-2222-3333-4444-555555555555"),
        "church-b" => new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        "contact-1" => new Guid("22222222-3333-4444-5555-666666666666"),
        "channel-a" => new Guid("33333333-4444-5555-6666-777777777777"),
        _ => throw new InvalidDataException("UnknownLocalizationAlias"),
    };

    /// <summary>
    /// 單案儲存替身只擷取真正 Writer 的不可變命令及次數，回固定合成正式列 ID；
    /// 不模擬 D365 欄位映射、唯一鍵防重或真實資料庫寫入，沒有連線或需排空工作。
    /// </summary>
    private sealed class CapturingStore : IOutboundEnqueueStore
    {
        /// <summary>當案最後一次命令，僅保留至測試結束，不跨案共享。</summary>
        internal OutboundTaskCommand? Command { get; private set; }
        /// <summary>決定性入列計數，預期成功案例只入場一次。</summary>
        internal int Calls { get; private set; }

        /// <summary>觀測正式 Writer 產物；先檢查 caller 取消，再回不等於來源鍵的合成資料列主鍵。</summary>
        public Guid CreateOrGet(OutboundTaskCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Command = command;
            return new Guid("44444444-5555-6666-7777-888888888888");
        }
    }

    /// <summary>
    /// 單案來源替身只依正式 seed／trigger 回傳新純值，沒有 SDK、憑證、共享快取或
    /// Dispose 資源。計數與 JSON 參照只活到該案 await 結束，不替產品決定語系或 alias。
    /// </summary>
    /// <param name="scenario">只由本次測試持有的合成情境。</param>
    private sealed class ScenarioSource(JObject scenario) : ILineRecipientSource
    {
        /// <summary>當案教會輸入，不是已授權設定；真正設定政策由產品服務判斷。</summary>
        internal JObject ChurchFields { get; } = Seed(scenario, "church-a");
        /// <summary>當案會友讀取次數，用來判斷先驗檢查及非預期重讀。</summary>
        internal int MemberReads { get; private set; }
        /// <summary>當案頻道讀取次數；未綁定應為零，防止拒絕後仍讀取憑證來源。</summary>
        internal int ChannelReads { get; private set; }

        /// <summary>檢查正式 Contact 主鍵與取消，將明確 LINE marker 解碼為固定合成身分。</summary>
        public MemberRecipient ReadMember(Guid contactId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(Identity("contact-1"), contactId);
            MemberReads++;
            var fields = Seed(scenario, "contact-1");
            var marker = fields["new_lineid"];
            Assert.NotNull(marker);
            if (marker?.Type != JTokenType.Null)
                Assert.Equal("contact-1", (string?)marker!["line"]);
            return new MemberRecipient(contactId,
                marker?.Type == JTokenType.Null ? null : "U11111111111111111111111111111111",
                Identity(Text(fields["parentcustomerid"]!, "ref")),
                Text(scenario["trigger"]!["fields"]!, "new_preferredlanguage"));
        }

        /// <summary>回傳明列頻道主鍵與無效密文；Shadow 不得解密，語系政策仍使用真實 resolver。</summary>
        public ChurchChannelRecord ReadChannel(Guid sourceChurchId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(Identity("church-a"), sourceChurchId);
            ChannelReads++;
            var channel = Identity(Text(ChurchFields["new_linechannelid"]!, "ref"));
            return new ChurchChannelRecord(sourceChurchId, channel, channel, "Synthetic Church",
                "invalid-shadow-ciphertext", new ChurchLocaleSettings(Text(ChurchFields, "new_defaultlanguage"),
                    Text(ChurchFields, "new_enabledlanguages").Split(',')));
        }

        /// <summary>以唯一 seed 別名讀取欄位；缺漏與重複直接失敗，不依陣列位置或顯示名稱覆寫。</summary>
        private static JObject Seed(JObject value, string alias) =>
            Assert.IsType<JObject>(Assert.Single(Assert.IsType<JArray>(value["seed"]),
                item => Text(item, "alias") == alias)["fields"]);
    }

    /// <summary>由單案 using 擁有的離線 HTTP 拒絕器；不保存 request 或回應，任何呼叫都是失敗。</summary>
    private sealed class RejectHttpHandler : HttpMessageHandler
    {
        /// <summary>只保存當案傳輸次數，不保存 URI、認證、內容或可處置物件。</summary>
        internal int Calls { get; private set; }

        /// <summary>注入禁止網路的確定性失敗；測試必須證明預期所有分支都維持零呼叫。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("LocalizationScenarioAttemptedHttp");
        }
    }
}
