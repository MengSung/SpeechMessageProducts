/*
 * 檔案整體說明
 *
 * 目的與定位：P2-06 搬移前的主方案送訊特性基線，以實際四個相容類別與假 HTTP handler
 * 捕捉 URL、遮罩標頭及完整本文，不呼叫真實 LINE、CRM 或主機檔案圖片。此基線不是
 * 舊方案每個方法都已移植的證明；產品-only／外部未用 demo／SDK 差異逐項列研究清單。
 * 主要功能與流程：列舉明確納管的公開方法與多載，合成 SDK 訊息／事件，呼叫實際方法，
 * 與唯讀黃金逐位元比較。隨機貼圖先驗證範圍及 Content-Length 正確，僅遮罩 ID 與其衍生長度。
 * 使用邊界與維護：每案例獨佔無 cookie 的假 handler、SDK client、區域 CRM 替身及 menu
 * cache/state；只保存合成遮罩文字，HTTP 回應及圖片由案例 finally 排空與 Dispose。
 * ToolUtility 舊建構式會觸發程序 Factory；基線階段曾只注入已知欄位而未讀 credential，
 * 收斂後改用公開借用資源建構式，不修改 Factory static，也不宣稱測過舊建構式。
 * 內容故障案例斷言 SDK response 在解碼失敗後釋放；原 HEAD RichMenu 方法以圖片接縫
 * 補錄成功 wire 及未釋放圖片紅燈，最終 using 修復。沒有網路或實機驗收。
 */
using System.Net;
using System.Reflection;
using System.Text;
using ChurchReport.Tools;
using Line.Messaging;
using Line.Messaging.Webhooks;
using LineMessagingProcessor;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;
using ProductPush = ChurchReport.Tools.PushUtility;
using ProductReply = ChurchReport.Tools.ReplyUtility;
using ProductLine = ChurchReport.Tools.LineUtilityClass;
using LegacyPush = ToolUtility.PushUtility;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>
/// 保護現有入口逐一送出的真實 SDK 協定，不讓重構把 multicast、split push、reply 或 menu
/// 誤當成相同路徑；假的 HTTP 及 CRM 都由本案例 owns，基線檔永遠只讀、不在測試自動更新。
/// </summary>
public sealed class P206SendUtilityGoldenTests
{
    private const string UserId = "U11111111111111111111111111111111";
    private const string OtherUserId = "U22222222222222222222222222222222";

    /// <summary>明確列出既有送訊方法；同名多載各自列舉，新增公開方法不會自動取得黃金豁免。</summary>
    public static IEnumerable<object[]> Cases()
    {
        var types = new[] { ("push", typeof(ProductPush)), ("reply", typeof(ProductReply)),
            ("line", typeof(ProductLine)), ("tool", typeof(LegacyPush)) };
        foreach (var (owner, type) in types)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(IsMessageMethod).OrderBy(MethodKey, StringComparer.Ordinal))
            {
                yield return new object[] { owner, MethodKey(method), "default" };
                if (method.Name == "EchoAsyncProcessor")
                {
                    yield return new object[] { owner, MethodKey(method), "group" };
                    yield return new object[] { owner, MethodKey(method), "room" };
                }
            }
        }
    }

    /// <summary>
    /// 用合成輸入呼叫原公開方法並精確比較遮罩後的 wire golden。注入的 menu cache 命中，
    /// 避免產品固定磁碟圖檔；仍經真實 assignment workflow 與 SDK link/unlink HTTP。
    /// </summary>
    /// <param name="owner">四個實際類別來源。</param>
    /// <param name="methodKey">含參數型別的公開多載唯一鍵。</param>
    /// <param name="variant">回音的 user/group/room 分支。</param>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PublicMethod_SyntheticHandler_MatchesPreMigrationGolden(string owner, string methodKey, string variant)
    {
        using var handler = new RecordingHandler("token-synthetic-" + owner);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var sdk = new LineMessagingClient(http, "token-synthetic-" + owner);
        using var tracer = new NullToolUtilityTracer();
        var crm = DispatchProxy.Create<IOrganizationService, EmptyCrm>();
        using var tool = new ToolUtilityClass(crm, tracer, new ConfigurationBuilder().Build());
        var cache = new InMemoryLineRichMenuIdCache();
        cache.Set("legacy-auth", "richmenu-fixed");
        var assignment = new LineRichMenuAssignmentWorkflow(
            new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(sdk)),
            cache, new InMemoryRichMenuStateStore());
        using var line = new TestLine(tool, sdk, assignment);
        object utility = owner switch
        {
            "push" => new ProductPush(sdk, null, assignment),
            "reply" => new ProductReply(sdk),
            "line" => line,
            "tool" => CreateLegacyPush(sdk, tool),
            _ => throw new InvalidOperationException("Unknown fixture owner.")
        };
        var method = utility.GetType().GetMethods().Single(m => MethodKey(m) == methodKey);
        var arguments = method.GetParameters().Select(p => Argument(p, variant)).ToArray();
        var result = method.Invoke(utility, arguments);
        if (result is Task operation) await operation;
        var transcript = handler.Transcript(method.Name == "ReplyRandomStickerAsync");
        var caseKey = owner + "." + methodKey + "." + variant;
        Assert.Equal(ReadGolden(caseKey), transcript);
    }

    /// <summary>
    /// 注入非法圖片使 Image.FromStream 在取得 SDK stream 後失敗；斷言原例外保留且 response
    /// 在方法結束已 Dispose。finally 只為紅燈舊版清掉替身資源，避免測試自己重現洩漏後仍留存。
    /// </summary>
    [Fact]
    public async Task EchoImageAsync_DecodeFailure_ReleasesOwnedResponse()
    {
        using var handler = new RecordingHandler("synthetic", invalidImage: true);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var sdk = new LineMessagingClient(http, "synthetic");
        var utility = new ProductReply(sdk);
        var exception = await Record.ExceptionAsync(() => utility.EchoImageAsync("reply-fixed", "message-fixed", "unused"));
        Assert.NotNull(exception);
        Assert.Single(handler.Media);
        Assert.True(handler.Media[0].Released, "下載 response 必須在解碼失敗後由唯一 owner 確定性釋放。");
    }

    /// <summary>正常圖片故障點之外也必須釋放下載 response；無 blob owner，成功仍不新增回覆。</summary>
    [Fact]
    public async Task EchoImageAsync_Success_ReleasesOwnedResponse()
    {
        using var handler = new RecordingHandler("synthetic");
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var sdk = new LineMessagingClient(http, "synthetic");
        await new ProductReply(sdk).EchoImageAsync("reply-fixed", "message-fixed", "unused");
        Assert.True(Assert.Single(handler.Media).Released);
        Assert.Equal(ReadGolden("reply.EchoImageAsync(String,String,String).default"), handler.Transcript(false));
    }

    /// <summary>
    /// CRM-only 公開方法逐一呼叫，合成 contact／party 使用伺服器 Guid，不作正式授權替身。
    /// 斷言原組裝結果及精確 NO HTTP；不復活外部舊方案的純 CRM 功能。
    /// </summary>
    /// <param name="method">主方案保留的 CRM-only 方法名。</param>
    [Theory]
    [InlineData("SetupActionList")]
    [InlineData("ConvertActionType")]
    [InlineData("GetLineSender")]
    [InlineData("GetLineIdAndContactFullNameOfSender")]
    [InlineData("GetContactPartyFullName")]
    public void ProductCrmMethod_SyntheticRecord_DoesNotSendHttp(string method)
    {
        using var handler = new RecordingHandler("synthetic");
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var sdk = new LineMessagingClient(http, "synthetic");
        using var tracer = new NullToolUtilityTracer();
        using var tool = new ToolUtilityClass(DispatchProxy.Create<IOrganizationService, EmptyCrm>(), tracer, new ConfigurationBuilder().Build());
        using var line = new ProductLine(tool, sdk, null);
        var id = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var party = new Entity("activityparty") { ["partyid"] = new EntityReference("contact", id) { Name = "合成顯示名" } };
        var letter = new Entity("letter")
        {
            ["from"] = new EntityCollection(new List<Entity> { party }),
            ["new_displayed_lineid"] = "display-fixed", ["new_linereplytoken"] = "reply-fixed",
            ["new_template_label_1"] = "按鈕", ["new_action_category_1"] = new OptionSetValue(100000001),
            ["new_template_text_1"] = "本文", ["new_template_uri_1"] = "https://example.test/action"
        };
        var displayed = ""; var lineId = ""; var token = ""; var name = "";
        switch (method)
        {
            case "SetupActionList":
                var template = new TemplateMessageClass { template = new TemplateClass { actions = new List<ActionClass>() } };
                line.SetupActionList(letter, ref template);
                Assert.Equal("message", Assert.Single(template.template.actions).type);
                break;
            case "ConvertActionType": Assert.Equal("message", line.ConvertActionType(letter, "new_action_category_1")); break;
            case "GetLineSender": Assert.Equal(id, line.GetLineSender(letter).Id); break;
            case "GetLineIdAndContactFullNameOfSender":
                line.GetLineIdAndContactFullNameOfSender(letter, ref displayed, ref lineId, ref token, ref name);
                Assert.Equal(("display-fixed", UserId, "reply-fixed", "合成顯示名"), (displayed, lineId, token, name));
                break;
            case "GetContactPartyFullName":
                Assert.Equal("合成顯示名", line.GetContactPartyFullName(party, ref lineId));
                Assert.Equal(UserId, lineId);
                break;
        }
        Assert.Equal("NO HTTP\n", handler.Transcript(false));
    }

    /// <summary>四個舊類都應標明階段6移除；避免新呼叫端把相容殼當正式授權入口。</summary>
    [Fact]
    public void CompatibilityShells_AreObsolete()
    {
        foreach (var type in new[] { typeof(LegacyPush), typeof(ProductPush), typeof(ProductReply), typeof(ProductLine) })
            Assert.NotNull(type.GetCustomAttribute<ObsoleteAttribute>());
    }

    /// <summary>
    /// 重播最完整舊來源的直接 SDK 操作作為補充基線，並與新委派及唯讀黃金三方比較。
    /// 來源重播不是舊組件實機；保護 group、多播重複收件、Sender、Buttons 及原 RichMenu
    /// 四步成功路徑。圖片只由固定有限位元組建構，兩條路都斷言 using 釋放。
    /// </summary>
    /// <param name="capability">研究對照的明確保留能力。</param>
    [Theory]
    [InlineData("group-push")]
    [InlineData("multicast-duplicates")]
    [InlineData("reply-buttons")]
    [InlineData("reply-sender")]
    [InlineData("menu-add")]
    public async Task LegacyCapability_DirectSdkReplayAndDelegation_MatchGolden(string capability)
    {
        var baseline = await RunCapabilityAsync(capability, directSdk: true);
        var actual = await RunCapabilityAsync(capability, directSdk: false);
        Assert.Equal(baseline, actual);
        Assert.Equal(ReadGolden(capability, "P206-source-replay.http.golden"), actual);
    }

    /// <summary>
    /// 注入上傳／來源故障，驗證 upload 失敗後不得 link/notify，已開啟圖片必定釋放；
    /// 不把原例外改成 NotSupported，不加入產品未核定 rollback，亦不讀主機固定圖檔。
    /// </summary>
    /// <param name="sourceFailure">true 在開啟來源時失敗，false 在 upload handler 失敗。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyMenu_SourceOrUploadFailure_PreservesFailureAndReleases(bool sourceFailure)
    {
        var failure = new IOException("synthetic failure");
        using var handler = new RecordingHandler("synthetic", uploadFailure: sourceFailure ? null : failure);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var sdk = new LineMessagingClient(http, "synthetic");
        using var tracer = new NullToolUtilityTracer();
        using var tool = new ToolUtilityClass(DispatchProxy.Create<IOrganizationService, EmptyCrm>(), tracer, new ConfigurationBuilder().Build());
        var image = new TrackingStream(MenuImage());
        try
        {
            var utility = new LegacyPush(sdk, tool, () => sourceFailure ? throw failure : image);
            Assert.Same(failure, await Record.ExceptionAsync(() => utility.AddRichMenuMessage(UserId)));
            Assert.Equal(!sourceFailure, image.Released);
            var transcript = handler.Transcript(false);
            Assert.Contains("POST https://api.line.me/v2/bot/richmenu", transcript);
            Assert.DoesNotContain("/user/", transcript);
            Assert.DoesNotContain("/message/push", transcript);
        }
        finally { image.Dispose(); }
    }

    /// <summary>
    /// 兩頻道並行交替推播 40 次並下載 20 次；handler 嚴格核對 token，本文必須只含該
    /// 操作身分，所有媒體均在 owner 排空後釋放。有限次數不是正式長時間壓測。
    /// </summary>
    [Fact]
    public async Task LegacyAdapter_ConcurrentChannels_IsolatedAndDrained()
    {
        using var a = new RecordingHandler("channel-a");
        using var b = new RecordingHandler("channel-b");
        using var ha = new HttpClient(a) { Timeout = TimeSpan.FromSeconds(5) };
        using var hb = new HttpClient(b) { Timeout = TimeSpan.FromSeconds(5) };
        using var sa = new LineMessagingClient(ha, "channel-a");
        using var sb = new LineMessagingClient(hb, "channel-b");
        var pa = new LineMessagingProcessorClass(sa);
        var pb = new LineMessagingProcessorClass(sb);
        var wa = new LineLegacySendWorkflow(pa);
        var wb = new LineLegacySendWorkflow(pb);
        await Task.WhenAll(Enumerable.Range(0, 20).SelectMany(i => new[]
        {
            wa.SendAsync("Cgroup-a", new ISendMessage[] { new TextMessage("only-a-" + i) }),
            wb.SendAsync("Cgroup-b", new ISendMessage[] { new TextMessage("only-b-" + i) })
        }));
        await Task.WhenAll(Enumerable.Range(0, 10).SelectMany(_ => new[]
        {
            pa.UseContentAsync("media-a", stream => Assert.True(stream.CanRead)),
            pb.UseContentAsync("media-b", stream => Assert.True(stream.CanRead))
        }));
        Assert.DoesNotContain("only-b", a.Transcript(false));
        Assert.DoesNotContain("only-a", b.Transcript(false));
        Assert.Equal(10, a.Media.Count);
        Assert.Equal(10, b.Media.Count);
        Assert.All(a.Media.Concat(b.Media), media => Assert.True(media.Released));
    }

    /// <summary>固定微型 PNG 位元組是唯一圖片輸入；不依賴 GDI 編碼版本、磁碟或真實產品內容。</summary>
    private static byte[] MenuImage() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg==");

    /// <summary>複製主方案原選單定義用於來源重播；由測試明確鎖定，不呼叫被測的模型產生器。</summary>
    private static RichMenu BaselineMenu() => new()
    {
        Size = ImagemapSize.RichMenuLong, Selected = false, Name = "nice richmenu", ChatBarText = "touch me",
        Areas = new List<ActionArea> { new() { Bounds = new ImagemapArea(0, 0, 2500, 1686),
            Action = new PostbackTemplateAction("ButtonA", "Menu A", "Menu A") } }
    };

    /// <summary>每次 owns 獨立 SDK／假 CRM／handler；原直接呼叫與新入口不共用可變 payload 或憑證。</summary>
    private static async Task<string> RunCapabilityAsync(string capability, bool directSdk)
    {
        using var handler = new RecordingHandler("synthetic");
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var sdk = new LineMessagingClient(http, "synthetic");
        using var tracer = new NullToolUtilityTracer();
        using var tool = new ToolUtilityClass(DispatchProxy.Create<IOrganizationService, EmptyCrm>(), tracer, new ConfigurationBuilder().Build());
        var image = new TrackingStream(MenuImage());
        try
        {
            var legacy = new LegacyPush(sdk, tool, () => image);
            var messages = new List<ISendMessage> { new TextMessage("合成訊息"), new AudioMessage("https://example.test/audio", 1000) };
            switch (capability)
            {
                case "group-push":
                    if (directSdk) await sdk.PushMessageAsync("Cgroup-fixed", messages);
                    else await legacy.SendMessage("Cgroup-fixed", messages);
                    break;
                case "multicast-duplicates":
                    var recipients = new[] { UserId, OtherUserId, UserId };
                    if (directSdk) await sdk.MultiCastMessageAsync(recipients, new[] { new TextMessage("合成訊息") });
                    else await legacy.MultiCastTextMessageAsync(recipients, "合成訊息");
                    break;
                case "reply-buttons":
                    var buttons = new List<ISendMessage> { new TemplateMessage("替代文字", new ButtonsTemplate(
                        "本文", "標題", "https://example.test/thumb", new[] { new MessageTemplateAction("是", "同意") })) };
                    if (directSdk) await sdk.ReplyMessageAsync("reply-fixed", buttons);
                    else await new ProductReply(sdk).ReplyMessage("reply-fixed", buttons);
                    break;
                case "reply-sender":
                    var sender = new List<ISendMessage> { new TextMessage("合成訊息") { Sender = new Sender("品牌", "https://example.test/icon") } };
                    if (directSdk) await sdk.ReplyMessageAsync("reply-fixed", sender);
                    else await new ProductReply(sdk).ReplyMessage("reply-fixed", sender);
                    break;
                case "menu-add":
                    if (directSdk)
                    {
                        var id = await sdk.CreateRichMenuAsync(BaselineMenu());
                        using (image) await sdk.UploadRichMenuPngImageAsync(image, id);
                        await sdk.LinkRichMenuToUserAsync(UserId, id);
                        await sdk.PushMessageAsync(UserId, new ISendMessage[] { new TextMessage("Rich menu added"), new StickerMessage("1", "5") });
                    }
                    else Assert.Equal("成功", await legacy.AddRichMenuMessage(UserId));
                    Assert.True(image.Released);
                    break;
                default: throw new InvalidOperationException("Unknown capability fixture.");
            }
            return handler.Transcript(false);
        }
        finally { image.Dispose(); }
    }

    /// <summary>限制反射列舉在本包的送訊／下載範圍；CRM、設定及 Dispose 不假稱 HTTP。</summary>
    private static bool IsMessageMethod(MethodInfo method) =>
        method.Name.StartsWith("Send", StringComparison.Ordinal) ||
        method.Name.StartsWith("Reply", StringComparison.Ordinal) ||
        method.Name.StartsWith("PostSerialized", StringComparison.Ordinal) ||
        method.Name.StartsWith("Echo", StringComparison.Ordinal) ||
        method.Name is "MultiCastTextMessageAsync" or "AddRichMenuMessage" or "DeleteRichMenuMessage" or
            "UploadMediaContentAsync" or "ConfirmMessage" or "CarouselMessage" or "ChurchCarouselMessage" or
            "ConfirmMessageAsync" or "CarouselMessageAsync" or "ChurchCarouselMessageAsync";

    /// <summary>完整型別名形成同名多載鍵，不使用列號或顯示文字當身分。</summary>
    private static string MethodKey(MethodInfo method) => method.Name + "(" +
        string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name.Replace("`", "-", StringComparison.Ordinal))) + ")";

    /// <summary>每次建立新的合成訊息與集合，避免測試的可變 SDK 模型跨案例共享。</summary>
    private static object Argument(ParameterInfo parameter, string variant)
    {
        if (parameter.ParameterType == typeof(string))
        {
            return parameter.Name!.ToLowerInvariant() switch
            {
                "userid" => UserId,
                "replytoken" => "reply-fixed",
                "retrykey" => "33333333-3333-3333-3333-333333333333",
                "messageid" => "message-fixed",
                "originalcontenurl" => "https://example.test/original",
                "previewimageurl" => "https://example.test/preview",
                "thumbnailimageurl" => "https://example.test/thumb",
                "imageurl" => "https://example.test/imagemap",
                "alttext" => "替代文字",
                "title" => "標題",
                "address" => "合成地址",
                "blobdirectoryname" or "blobname" => "unused",
                _ => "合成訊息"
            };
        }
        if (parameter.ParameterType == typeof(int)) return parameter.Name is "BaseWidth" or "Basehight" ? 1040 : 1;
        if (parameter.ParameterType == typeof(long)) return 1000L;
        if (parameter.ParameterType == typeof(decimal)) return 25.0m;
        if (parameter.ParameterType == typeof(Entity)) return new Entity("letter", Guid.Parse("44444444-4444-4444-4444-444444444444"));
        if (parameter.ParameterType == typeof(FlexMessage)) return FlexMessage.CreateBubbleMessage("替代文字");
        if (parameter.ParameterType == typeof(MessageEvent))
        {
            var kind = variant switch { "group" => EventSourceType.Group, "room" => EventSourceType.Room, _ => EventSourceType.User };
            var source = new WebhookEventSource(kind, variant == "default" ? UserId : variant + "-fixed", UserId);
            return new MessageEvent(source, 0, new TextEventMessage("message-fixed", "合成訊息"), "reply-fixed");
        }
        if (parameter.ParameterType == typeof(List<ITemplateAction>))
            return new List<ITemplateAction> { new MessageTemplateAction("是", "同意"), new MessageTemplateAction("否", "拒絕") };
        if (parameter.ParameterType == typeof(List<IImagemapAction>))
            return new List<IImagemapAction> { new MessageImagemapAction(new ImagemapArea(0, 0, 100, 100), "點擊") };
        if (parameter.ParameterType == typeof(IList<string>)) return new List<string> { UserId, OtherUserId };
        if (parameter.ParameterType == typeof(List<ISendMessage>) || parameter.ParameterType == typeof(IReadOnlyList<ISendMessage>))
            return new List<ISendMessage> { new TextMessage("合成訊息"), new StickerMessage("1", "5") };
        throw new InvalidOperationException("Fixture argument missing: " + parameter.ParameterType.Name);
    }

    /// <summary>
    /// 改用收斂後的公開借用資源建構式；fake CRM 與 SDK 均由該案例擁有，
    /// 不碰程序 Factory 或 private credential；原始基線在產品搬移前已封存。
    /// </summary>
    private static LegacyPush CreateLegacyPush(LineMessagingClient sdk, ToolUtilityClass tool)
    {
        return new LegacyPush(sdk, tool, () => new MemoryStream(MenuImage(), writable: false));
    }

    /// <summary>由方案檔定位本 clone 的唯讀 fixture，不使用個人電腦的固定路徑。</summary>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new InvalidOperationException("Fixture repository root not found.");
    }

    /// <summary>只取唯一 case 區段；缺失／重複鍵立即失敗，不用最後一筆覆蓋掩飾問題。</summary>
    private static string ReadGolden(string key, string fileName = "P206-pre-migration.http.golden")
    {
        var path = Path.Combine(RepositoryRoot(), "ChurchReport.MemberInfo.Tests", "LineSharedWorkflow", "Golden", fileName);
        var sections = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split("@@ ", StringSplitOptions.RemoveEmptyEntries);
        var section = Assert.Single(sections.Where(s => s.StartsWith(key + "\n", StringComparison.Ordinal)));
        return section[(key.Length + 1)..];
    }

    /// <summary>只暴露既有受保護注入路徑；不建立 CRM client、不修改產品型別或 token owner。</summary>
    private sealed class TestLine : ProductLine
    {
        /// <summary>借用案例 SDK／工具；本例 Dispose 只關閉自身，不會提前關閉借來的資源。</summary>
        public TestLine(ToolUtilityClass tool, LineMessagingClient sdk, ILineRichMenuAssignmentWorkflow assignment)
            : base(tool, sdk, null, null, null, assignment, (_, _, _) => { }) { }
    }

    /// <summary>合成 CRM 回傳固定 contact 或空統計查詢；任何寫入或未預期操作失敗，不連線且無外部資源。</summary>
    public class EmptyCrm : DispatchProxy
    {
        /// <summary>僅允許空統計查詢及固定 contact 讀取；未預期動作失敗，不偷偷建立真實連線。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IOrganizationService.RetrieveMultiple) => new EntityCollection(),
            nameof(IOrganizationService.Retrieve) => new Entity("contact", (Guid)args![1]!) { ["new_lineid"] = UserId },
            _ => throw new InvalidOperationException("Unexpected synthetic CRM operation.")
        };
    }

    /// <summary>有限長度圖片的唯一 using owner；僅有釋放旗標，不保存產品或 request 狀態。</summary>
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        /// <summary>不依賴 GC 的確定性釋放觀察。</summary>
        internal bool Released { get; private set; }
        /// <summary>釋放後保留旗標供斷言，其餘交基底回收；測試 finally 可安全重複呼叫。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Released = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>追蹤內容owner只記錄釋放旗標，不保存 request 或憑證；handler finally 回收紅燈遺漏。</summary>
    private sealed class TrackingContent(byte[] bytes) : ByteArrayContent(bytes)
    {
        /// <summary>確定性釋放斷言，不依賴 GC／終結器判斷洩漏。</summary>
        internal bool Released { get; private set; }
        /// <summary>先記錄再交基底關閉內容與串流；重複 Dispose 仍安全。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Released = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>固定合成回應，僅保存遮罩 wire 字串；每案例最多數筆，Dispose 清掉記錄並關閉所有媒體owner。</summary>
    private sealed class RecordingHandler(string expectedToken, bool invalidImage = false, Exception? uploadFailure = null) : HttpMessageHandler
    {
        private readonly List<string> requests = new();
        /// <summary>只保留本案例回應內容以斷言 owner；案例 Dispose 清掉引用。</summary>
        internal List<TrackingContent> Media { get; } = new();

        /// <summary>捕捉真實 SDK request；回應無外部網路，所有 token 都是案例擁有的合成字串。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(expectedToken, request.Headers.Authorization?.Parameter);
            var headers = request.Headers.Select(h => h.Key + ": " + (h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                ? "Bearer ***" : string.Join(",", h.Value)))
                .Concat(request.Content?.Headers.Select(h => h.Key + ": " + string.Join(",", h.Value)) ?? Enumerable.Empty<string>())
                .OrderBy(h => h, StringComparer.Ordinal);
            var binary = request.Content?.Headers.ContentType?.MediaType == "image/png";
            var body = request.Content == null ? "" : binary
                ? "base64:" + Convert.ToBase64String(await request.Content.ReadAsByteArrayAsync(cancellationToken))
                : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (requests) requests.Add(request.Method + " " + request.RequestUri + "\n" + string.Join("\n", headers) + "\n\n" + body + "\n");
            if (binary && uploadFailure != null) throw uploadFailure;
            HttpContent content;
            if (request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal))
            {
                using var image = new Image<Rgba32>(8, 8);
                using var memory = new MemoryStream();
                image.SaveAsPng(memory);
                var media = new TrackingContent(invalidImage ? new byte[] { 1, 2, 3 } : memory.ToArray());
                media.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                lock (Media) Media.Add(media);
                content = media;
            }
            else
            {
                var json = request.RequestUri.AbsolutePath.Contains("/member/", StringComparison.Ordinal)
                    ? "{\"displayName\":\"合成會友\",\"userId\":\"" + UserId + "\"}"
                    : "{\"richMenuId\":\"richmenu-fixed\"}";
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        /// <summary>隨機貼圖先驗範圍與原 Content-Length，再遮罩 ID 及衍生長度；其他請求不正規化。</summary>
        internal string Transcript(bool randomSticker)
        {
            var transcript = string.Join("---\n", requests);
            if (randomSticker)
            {
                var offset = transcript.IndexOf("{", StringComparison.Ordinal);
                var body = JObject.Parse(transcript[offset..]);
                var id = int.Parse(body["messages"]![0]!["stickerId"]!.Value<string>()!, System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(id is >= 1 and <= 17 or 21 or >= 100 and <= 139 or >= 401 and <= 430);
                var lengthLine = transcript.Split('\n').Single(line => line.StartsWith("Content-Length: ", StringComparison.Ordinal));
                Assert.Equal(Encoding.UTF8.GetByteCount(transcript[offset..].TrimEnd('\n')),
                    int.Parse(lengthLine["Content-Length: ".Length..], System.Globalization.CultureInfo.InvariantCulture));
                transcript = transcript.Replace(lengthLine, "Content-Length: <random-body-length>", StringComparison.Ordinal);
                offset = transcript.IndexOf("{", StringComparison.Ordinal);
                body["messages"]![0]!["stickerId"] = "<random-existing-range>";
                transcript = transcript[..offset] + body.ToString(Formatting.None) + "\n";
            }
            return requests.Count == 0 ? "NO HTTP\n" : transcript;
        }

        /// <summary>只在該案例排空後關閉尚未釋放的媒體，並清空捕捉集合；不與其他案例共用。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var content in Media) content.Dispose();
                Media.Clear();
                requests.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
