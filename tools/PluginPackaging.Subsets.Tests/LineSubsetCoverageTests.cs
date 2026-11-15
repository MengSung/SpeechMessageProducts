/*
 * 檔案整體說明
 * 目的與定位：以實際Line.Messaging.PluginSubset輸出證明Flex、Quick Reply、Sender、模板動作、列舉與converter可執行。
 * 主要功能與流程：建立操作區域物件圖，使用正式Newtonsoft序列化／反序列化並斷言協定值；
 * 另直接呼叫真實URI與Postback建構子組合簽核Flex，保護hero桌面連結與footer同意／退回資料不被投影遺漏。
 * 驗證與失敗：以固定合成網址、正式ID格式與繁中顯示文字斷言JSON精確路徑，並核對動作與TextMessage
 * 來自同一實際子集組件且不參考Microsoft.CSharp；漏連結來源、建構子缺失或協定值改變均應使回歸失敗。
 * 使用邊界與維護：模型與序列化器僅存於單一測試；不建HttpClient、不連LINE，沒有憑證或外部I/O。
 * 每次案例建立新物件，由測試持有至同步斷言結束後即可回收；不建立需Dispose的資源、背景工作或跨案例快取。
 * 測試host以Coverlet收集Link子集assembly，不混入原始SDK DLL的覆蓋率；離線序列化不代表實機通知送達。
 */
using System;
using System.Collections.Generic;
using Line.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace SpeechMessage.Tools.PluginPackaging.Subsets.Tests;

/// <summary>走訪沙箱所需模型閉包，真序列化／converter斷言防止只以型別存在充當覆蓋。</summary>
public sealed class LineSubsetCoverageTests
{
    /// <summary>完整SDK的HTTP client不屬核定投影；若相依複製覆蓋子集，必須拒絕這份coverage。</summary>
    [Fact]
    public void LoadedAssembly_IsTheActualNet462SubsetNotTheFullSdk()
    {
        var assembly = typeof(TextMessage).Assembly;
        Assert.Null(assembly.GetType("Line.Messaging.LineMessagingClient"));
        Assert.Equal(".NETFramework,Version=v4.6.2",
            assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
                .Cast<System.Runtime.Versioning.TargetFrameworkAttribute>().Single().FrameworkName);
    }

    /// <summary>建立Flex全部元件、樣式、enum與quick reply，真JSON往返覆蓋來源投影。</summary>
    [Fact]
    public void LinkedFlexGraph_SerializesAllSubsetModelsAndConverters()
    {
        var action = new MessageAction("確認", "CONFIRM");
        var quick = new QuickReply(new List<QuickReplyButtonObject>
        {
            new(action, "https://example.invalid/icon.png")
        });
        var bubble = new BubbleContainer
        {
            Direction = ComponentDirection.Ltr,
            Header = new BoxComponent(BoxLayout.Horizontal)
            {
                Contents = new List<IFlexComponent> { new TextComponent("標題") { Size = ComponentSize._3xl, Weight = Weight.Bold } }
            },
            Hero = new ImageComponent("https://example.invalid/hero.png")
            {
                AspectRatio = AspectRatio._16_9,
                AspectMode = AspectMode.Cover,
                BackgroundColor = ColorCode.FromRgb(1, 2, 3),
                Action = action
            },
            Body = new BoxComponent(BoxLayout.Vertical)
            {
                Background = new Background { Angle = "90deg", StartColor = "#000000", EndColor = "#FFFFFF" },
                Spacing = Spacing.Md,
                Contents = new List<IFlexComponent>
                {
                    new ButtonComponent(action) { Height = ButtonHeight.Md, Style = ButtonStyle.Primary, Gravity = Gravity.Center },
                    new FillerComponent(),
                    new IconComponent("https://example.invalid/item.png") { Size = ComponentSize.Sm, AspectRatio = AspectRatio._1_1 },
                    new SeparatorComponent { Margin = Spacing.Xs, Color = "#CCCCCC" },
                    new SpacerComponent(ComponentSize.Xs),
                    new TextComponent("內文") { Align = Align.Center, Wrap = true, MaxLines = 2 }
                }
            },
            Footer = new BoxComponent { Contents = new List<IFlexComponent>() },
            Styles = new BubbleStyles { Body = new BlockStyle { BackgroundColor = "#FFFFFF", Separator = true, SeparatorColor = "#000000" } }
        };
        var bubbleMessage = FlexMessage.CreateBubbleMessage("bubble").SetBubbleContainer(bubble).SetQuickReply(quick);
        var carousel = (CarouselContainerFlexMessage)FlexMessage.CreateCarouselMessage("carousel");
        carousel.AddBubbleContainer(bubble).SetQuickReply(quick);
        var messages = new ISendMessage[] { new TextMessage("純文字", quick) { Sender = new Sender("教會", "https://example.invalid/logo.png") }, bubbleMessage, carousel };

        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter { NamingStrategy = new CamelCaseNamingStrategy() } }
        };
        var json = JsonConvert.SerializeObject(messages, settings);
        Assert.Contains("\"type\":\"flex\"", json, StringComparison.Ordinal);
        Assert.Contains("\"3xl\"", json, StringComparison.Ordinal);
        Assert.Equal("\"richmenuswitch\"", JsonConvert.SerializeObject(TemplateActionType.RichMenuSwitch, settings));
        Assert.Equal("2:1", new AspectRatio(2, 1).ToString());
        Assert.Equal("#010203", bubble.Hero.BackgroundColor);
        Assert.Equal("#0000FF", ColorCode.Blue);
        Assert.Equal("https://example.invalid/desktop", new AltUri("https://example.invalid/desktop").Desktop);
        Assert.Equal(TemplateActionType.Clipboard, JsonConvert.DeserializeObject<TemplateActionType>("\"clipboard\"", settings));
    }

    /// <summary>
    /// 保護P5-08簽核所需的真實子集建構子與序列化契約：hero保留URI及桌面替代連結，
    /// footer分別保留同意／退回的原始Data與DisplayText，且兩種動作均由TextMessage所在子集提供。
    /// 使用單案合成資料直接呼叫正式建構子，不使用Moq或動態繫結；來源漏投影時應於編譯或型別載入失敗，
    /// 以精確JSON值及組件參考斷言識別契約退化與意外引入Microsoft.CSharp。
    /// 所有物件僅由本同步案例持有至結束，不開連線、不保存使用者狀態，也沒有需排空或Dispose的資源。
    /// </summary>
    [Fact]
    public void LinkedSigningActions_PreserveHeroUrisAndFooterPostbacksWithoutDynamicBinder()
    {
        const string imageUrl = "https://example.invalid/appointment.png";
        const string desktopUrl = "https://example.invalid/desktop/appointment";
        const string approveData = "Action=同意&EntityId=11111111-2222-3333-4444-555555555555&Signer=測試簽核者";
        const string rejectData = "Action=退回&EntityId=11111111-2222-3333-4444-555555555555&Signer=測試簽核者";
        const string approveDisplayText = "同意此預約申請";
        const string rejectDisplayText = "退回此預約申請";
        var uriAction = new UriTemplateAction("開啟預約", imageUrl, new AltUri(desktopUrl));
        var approveAction = new PostbackTemplateAction("同意", approveData, approveDisplayText);
        var rejectAction = new PostbackTemplateAction("退回", rejectData, rejectDisplayText);
        var bubble = new BubbleContainer
        {
            Hero = new ImageComponent(imageUrl) { Action = uriAction },
            Footer = new BoxComponent(BoxLayout.Vertical)
            {
                Contents = new List<IFlexComponent>
                {
                    new ButtonComponent(approveAction),
                    new ButtonComponent(rejectAction)
                }
            }
        };
        var message = FlexMessage.CreateBubbleMessage("預約簽核").SetBubbleContainer(bubble);
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter { NamingStrategy = new CamelCaseNamingStrategy() } }
        };
        var json = JObject.Parse(JsonConvert.SerializeObject(message, settings));

        Assert.Equal("flex", (string?)json.SelectToken("type"));
        Assert.Equal("uri", (string?)json.SelectToken("contents.hero.action.type"));
        Assert.Equal(imageUrl, (string?)json.SelectToken("contents.hero.action.uri"));
        Assert.Equal(desktopUrl, (string?)json.SelectToken("contents.hero.action.altUri.desktop"));
        Assert.Equal("postback", (string?)json.SelectToken("contents.footer.contents[0].action.type"));
        Assert.Equal("同意", (string?)json.SelectToken("contents.footer.contents[0].action.label"));
        Assert.Equal(approveData, (string?)json.SelectToken("contents.footer.contents[0].action.data"));
        Assert.Equal(approveDisplayText, (string?)json.SelectToken("contents.footer.contents[0].action.displayText"));
        Assert.Equal("postback", (string?)json.SelectToken("contents.footer.contents[1].action.type"));
        Assert.Equal("退回", (string?)json.SelectToken("contents.footer.contents[1].action.label"));
        Assert.Equal(rejectData, (string?)json.SelectToken("contents.footer.contents[1].action.data"));
        Assert.Equal(rejectDisplayText, (string?)json.SelectToken("contents.footer.contents[1].action.displayText"));

        var assembly = typeof(TextMessage).Assembly;
        Assert.Same(assembly, uriAction.GetType().Assembly);
        Assert.Same(assembly, approveAction.GetType().Assembly);
        Assert.Same(assembly, rejectAction.GetType().Assembly);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Microsoft.CSharp", StringComparison.Ordinal));
    }

    /// <summary>自有合成模板動作，不引入SDK或可變全域狀態；隨單案物件圖釋放。</summary>
    private sealed class MessageAction : ITemplateAction
    {
        /// <summary>建立固定label/value的本案動作，不保留外部服務或憑證。</summary>
        internal MessageAction(string label, string value) { Label = label; Value = value; }
        /// <summary>序列化用穩定動作種類，供真JSON協定斷言使用。</summary>
        public TemplateActionType Type => TemplateActionType.Message;
        /// <summary>本案合成顯示文字，並非真實使用者資料。</summary>
        public string Label { get; }
        /// <summary>本案合成回傳值，只在此測試的序列化期間保留。</summary>
        public string Value { get; }
    }
}
