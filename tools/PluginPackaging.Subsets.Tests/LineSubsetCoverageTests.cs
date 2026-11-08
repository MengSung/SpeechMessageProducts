/*
 * 檔案整體說明
 * 目的與定位：以實際Line.Messaging.PluginSubset輸出證明Flex、Quick Reply、Sender、模板動作、列舉與converter可執行。
 * 主要功能與流程：建立一個涵蓋所有連結模型的操作區域物件圖，使用正式Newtonsoft序列化／反序列化並斷言協定值。
 * 使用邊界與維護：模型與序列化器僅存於單一測試；不建HttpClient、不連LINE，沒有憑證或外部I/O。
 * 每次案例建立新物件，完成後即釋放；測試host以Coverlet收集Link子集assembly，不混入原始SDK DLL的覆蓋率。
 */
using System;
using System.Collections.Generic;
using Line.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
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
