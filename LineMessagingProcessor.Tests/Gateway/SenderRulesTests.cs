/*
檔案整體說明
目的與定位：F5 sender 與 Flex 替代文字的離線純值契約，不建立 LINE、Dataverse 或主機。
主要功能與流程：以合成 JSON 注入冒名 sender、過長文字、非法圖示及 surrogate 邊界，
斷言權威租戶名稱、1500 上限與深層快照；未實作時由型別存在性斷言提供確定 RED。
使用邊界與維護：所有 JToken 由單一測試擁有，返回後即可回收，沒有背景工作或外借資源。
反射只用於新公開純函數的存在性及行為驗證，並非繞過任何禁止 API 或授權邊界。
*/
using System.Reflection;
using LineMessagingProcessor.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LineMessagingProcessor.Tests.Gateway;

/// <summary>直接檢查正式組件內的純函數，輸入均為合成資料，無網路副作用。</summary>
public sealed class SenderRulesTests
{
    /// <summary>注入 caller 冒名 sender；斷言新快照覆寫權威名稱、圖示且不改原樹。</summary>
    [Fact]
    public void Sender_AuthoritativeNameAndIcon_DetachedSnapshot()
    {
        var input = JArray.Parse("[{\"type\":\"text\",\"text\":\"hi\",\"sender\":{\"name\":\"forged\"}}]");
        var before = input.ToString();
        var result = Apply(input, "恩典教會", "https://example.test/icon.png");
        Assert.Equal("恩典教會", (string?)result[0]["sender"]?["name"]);
        Assert.Equal("https://example.test/icon.png", (string?)result[0]["sender"]?["iconUrl"]);
        result[0]["text"] = "changed";
        Assert.Equal(before, input.ToString());
    }

    /// <summary>名稱含 LINE 必須移除 caller sender；沒有有效圖示時只帶名稱，不向外下載。</summary>
    [Theory]
    [InlineData("教會Line測試", "http://example.test/a", true)]
    [InlineData("普通教會", "http://example.test/a", false)]
    [InlineData("普通教會", "https://user:pass@example.test/a", false)]
    public void Sender_UnsafeNameOrIcon_CannotRetainCallerBrand(string name, string icon, bool omit)
    {
        var result = Apply(JArray.Parse("[{\"type\":\"text\",\"text\":\"x\",\"sender\":{\"name\":\"forged\",\"iconUrl\":\"https://wrong.test\"}}]"), name, icon);
        if (omit) Assert.Null(result[0]["sender"]);
        else
        {
            Assert.Equal(name, (string?)result[0]["sender"]?["name"]);
            Assert.Null(result[0]["sender"]?["iconUrl"]);
        }
    }

    /// <summary>20 UTF-16 邊界不切破 surrogate；Flex 仍以完整權威名稱作前綴。</summary>
    [Fact]
    public void Sender_Over20_DoesNotSplitSurrogate()
    {
        var name = new string('甲', 19) + "\U0001F600" + "乙";
        var result = Apply(JArray.Parse("[{\"type\":\"flex\",\"altText\":\"提醒\",\"contents\":{}}]"), name, null);
        Assert.Equal(new string('甲', 19), (string?)result[0]["sender"]?["name"]);
        Assert.Equal("【" + name + "】提醒", (string?)result[0]["altText"]);
    }

    /// <summary>純文字21字必須保留恰好前20字，不能以surrogate案例意外接受19字的縮短回歸。</summary>
    [Fact]
    public void Sender_PlainNameOver20_TruncatesExactly20()
    {
        var name = new string('甲', 20) + "乙";
        var result = Apply(JArray.Parse("[{\"type\":\"text\",\"text\":\"提醒\"}]"), name, null);
        Assert.Equal(new string('甲', 20), (string?)result[0]["sender"]?["name"]);
    }

    /// <summary>前綴只加一次，核定上限為 1500 而非舊 SDK 建構子的 400。</summary>
    [Theory]
    [InlineData("主日提醒", "【恩典教會】主日提醒")]
    [InlineData("【恩典教會】主日提醒", "【恩典教會】主日提醒")]
    public void FlexAltText_PrefixedOnce(string text, string expected)
    {
        var result = Apply(new JArray(new JObject { ["type"] = "flex", ["altText"] = text, ["contents"] = new JObject() }), "恩典教會", null);
        Assert.Equal(expected, (string?)result[0]["altText"]);
    }

    /// <summary>截斷只移除原文尾端，保留前綴並避免最後留下半個 surrogate。</summary>
    [Fact]
    public void FlexAltText_1500_PreservesPrefixAndUnicode()
    {
        var text = new string('甲', 1494) + "\U0001F600" + "乙";
        var result = Apply(new JArray(new JObject { ["type"] = "flex", ["altText"] = text }), "恩典", null);
        var actual = (string)result[0]["altText"]!;
        Assert.StartsWith("【恩典】", actual);
        Assert.Equal(1500, actual.Length);
        Assert.False(char.IsHighSurrogate(actual[actual.Length - 1]));
        Assert.True(actual.Length > 400);
    }

    /// <summary>4字前綴加1495字使surrogate跨1500邊界，必須整對移除，尾端不能留下high surrogate。</summary>
    [Fact]
    public void FlexAltText_SurrogateAcross1500Boundary_RemovesWholePair()
    {
        var text = new string('甲', 1495) + "\U0001F600" + "乙";
        var result = Apply(new JArray(new JObject { ["type"] = "flex", ["altText"] = text }), "恩典", null);
        Assert.Equal("【恩典】" + new string('甲', 1495), (string?)result[0]["altText"]);
    }

    /// <summary>僅於型別尚未存在時提供可執行 RED；取得正式公開方法後直接驗證其返回值。</summary>
    private static JArray Apply(JArray input, string name, string? icon)
    {
        var type = typeof(LinePushClient).Assembly.GetType("LineMessagingProcessor.Core.Gateway.SenderRules");
        Assert.NotNull(type);
        var method = type!.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<JArray>(method!.Invoke(null, new object?[] { input, name, icon }));
    }
}
