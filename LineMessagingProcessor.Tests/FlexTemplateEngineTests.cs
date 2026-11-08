/*
 * 檔案整體說明
 *
 * 目的與定位：P2-05跨目標Flex與sender離線契約，使用真字典、偽語系及JSON序列化，
 * 不以字串Contains冒充結構安全；沒有D365／LINE或真實個資。
 * 主要功能與流程：六個指定同名案例鎖住文字／值替換、跳脫、altText必要key、
 * sender fallback／拒絕、偽語系完整包框；另驗缺值、壞JSON及資料不二次解譯。
 * 使用邊界與維護：來源替身只回當次完整scope純字典，無cache或背景工作；
 * 所有模型與JSON只存活於單案，無需Dispose的owner。正式產品模板黃金另在
 * GoodShepherd測試依嵌入資源逐份驗證，這些結果不能冒充實機發送。
 */
using Line.Messaging;
using LineMessagingProcessor.Core.Templates;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SpeechMessage.Localization;
using Xunit;

namespace LineMessagingProcessor.Tests;

/// <summary>以真正JSON與字典驗範本不可注入、文化不改scope及固定sender預算。</summary>
public sealed class FlexTemplateEngineTests
{
    private const string Template = "{\"type\":\"flex\",\"altText\":\"{{t:Common.Diagnostics.Ping}}\",\"contents\":{\"type\":\"bubble\",\"body\":{\"type\":\"box\",\"layout\":\"vertical\",\"contents\":[{\"type\":\"text\",\"text\":\"{{t:Common.Diagnostics.Ping}}\"},{\"type\":\"text\",\"text\":\"{{v:name}}\"}]}}}";

    /// <summary>取字與資料分別替換，不修改來源資料；輸出為正式可序列化Flex模型。</summary>
    [Fact]
    public void Fill_TextAndValuePlaceholders()
    {
        var values = new Dictionary<string, string> { ["name"] = "Alpha" };
        var result = Json(new FlexTemplateEngine(Catalog()).Fill(Template, Context(), values));
        Assert.Equal("連線測試成功", result["altText"]!.Value<string>());
        Assert.Equal("Alpha", result["contents"]!["body"]!["contents"]![1]!["text"]!.Value<string>());
        Assert.Equal("Alpha", values["name"]);
    }

    /// <summary>引號、反斜線、換行及假屬性只可成為文字，真JSON解析後值須逐字相同。</summary>
    [Fact]
    public void Fill_ValueWithQuotesAndNewlines_EscapedValidJson()
    {
        const string data = "\"}\\n\\\"admin\\\":true,\\\\\r\n{{t:Common.Diagnostics.Ping}}";
        var result = Json(new FlexTemplateEngine(Catalog()).Fill(Template, Context(),
            new Dictionary<string, string> { ["name"] = data }));
        Assert.Equal(data, result["contents"]!["body"]!["contents"]![1]!["text"]!.Value<string>());
        Assert.Null(result["admin"]);
    }

    /// <summary>altText不存在、直接文字或v資料皆不能代替正式取字key。</summary>
    [Theory]
    [InlineData("{\"type\":\"flex\",\"contents\":{\"type\":\"bubble\"}}")]
    [InlineData("{\"type\":\"flex\",\"altText\":\"literal\",\"contents\":{\"type\":\"bubble\"}}")]
    [InlineData("{\"type\":\"flex\",\"altText\":\"{{v:name}}\",\"contents\":{\"type\":\"bubble\"}}")]
    public void Template_MissingAltText_Throws(string template) =>
        Assert.Throws<FormatException>(() => new FlexTemplateEngine(Catalog()).Fill(template, Context(),
            new Dictionary<string, string> { ["name"] = "Alpha" }));

    /// <summary>長教會名使用同scope字典短名，不能截斷成另一名稱或沿用另一教會。</summary>
    [Fact]
    public void Sender_LongChurchName_UsesShortName() =>
        Assert.Equal("Short", new SenderNameFactory(Catalog("Short")).Create(Context(), new string('A', 30)));

    /// <summary>覆寫短名仍超20時必須拒絕，HTTP呼叫者不能取得不合規sender。</summary>
    [Fact]
    public void Sender_StillTooLong_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            new SenderNameFactory(Catalog(new string('s', 21))).Create(Context(), new string('A', 30)));

    /// <summary>真qps資源使每個t文字及altText包框，v資料保持原值而不是假翻譯。</summary>
    [Fact]
    public void Fill_PseudoLocale_AllTextWrapped()
    {
        var result = Json(new FlexTemplateEngine(Catalog()).Fill(Template, Context("qps-ploc"),
            new Dictionary<string, string> { ["name"] = "Alpha" }));
        foreach (var text in new[] { result["altText"]!.Value<string>()!,
            result["contents"]!["body"]!["contents"]![0]!["text"]!.Value<string>()! })
        {
            Assert.StartsWith("［", text, StringComparison.Ordinal);
            Assert.EndsWith("］", text, StringComparison.Ordinal);
            Assert.Contains("ǂ", text, StringComparison.Ordinal);
        }
    }

    /// <summary>缺值、重複JSON鍵及未閉合標記都fail-closed，不回部分訊息。</summary>
    [Theory]
    [InlineData(Template)]
    [InlineData("{\"type\":\"flex\",\"type\":\"flex\",\"altText\":\"{{t:Common.Diagnostics.Ping}}\",\"contents\":{\"type\":\"bubble\"}}")]
    [InlineData("{\"type\":\"flex\",\"altText\":\"{{t:Common.Diagnostics.Ping}}\",\"contents\":{\"type\":\"bubble\",\"text\":\"{{v:missing\"}}")]
    public void Fill_InvalidOrMissingInput_Throws(string template) =>
        Assert.Throws<FormatException>(() => new FlexTemplateEngine(Catalog()).Fill(template, Context(),
            new Dictionary<string, string>()));

    /// <summary>真Core使用的相同序列化政策，雙宿主精確驗協定欄位而非CLR屬性名稱。</summary>
    internal static JObject Json(FlexMessage message) => JObject.Parse(JsonConvert.SerializeObject(message,
        new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) }
        }));
    /// <summary>組織／產品由合成Host固定；語言不能改教會scope。</summary>
    private static TextContext Context(string culture = "zh-TW") =>
        new(Guid.NewGuid(), culture, culture == "qps-ploc" ? "en-US" : "zh-TW", "TestOrg", "GoodShepherd");
    /// <summary>真字典只在短名測試注入覆寫，其餘走正式生成資源。</summary>
    private static ITextCatalog Catalog(string? shortName = null) => new TextCatalog(new SystemTextTable(), new Overrides(shortName));
    /// <summary>無快取覆寫來源，回應必須複製完整原scope，沒有Session或SDK。</summary>
    private sealed class Overrides(string? shortName) : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>依單案參數注入短名，建立的純值不被提供者保存。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, shortName is null ? Array.Empty<ChurchTextOverride>() :
                new[] { new ChurchTextOverride(Guid.NewGuid(), context.ChurchId, new TextKey("Brand.ShortName"), "*", shortName) });
    }
}
