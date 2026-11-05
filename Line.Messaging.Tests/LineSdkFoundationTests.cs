using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhooks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 以離線官方形狀樣本與 RFC HMAC 向量保護 P1-03；樣本全為合成值，不連 LINE、不含實際收件者。
/// 相同案例在 net48 宿主（載入 net462）及 net10 上執行，防止僅編譯成功卻遺漏 Framework 行為。
/// </summary>
public sealed class LineSdkFoundationTests
{
    /// <summary>檢查實際載入 DLL 的框架屬性；不能把 net48 測試宿主誤算成產品 net48。</summary>
    [Fact]
    public void Assembly_TargetFramework_MatchesProduct()
    {
        var framework = typeof(LineMessagingClient).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false).Cast<TargetFrameworkAttribute>().Single();
#if NETFRAMEWORK
        Assert.Equal(".NETFramework,Version=v4.6.2", framework.FrameworkName);
#else
        Assert.Equal(".NETCoreApp,Version=v10.0", framework.FrameworkName);
#endif
    }

    /// <summary>建構時拒絕空或超過二十 UTF-16 單元的名稱；不可等遠端 API 才發現錯誤。</summary>
    /// <param name="length">名稱長度的邊界反例。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Sender_InvalidNameLength_Throws(int length) => Assert.Throws<ArgumentException>(() => new Sender(new string('a', length), "https://example.invalid/icon.png"));

    /// <summary>圖示網址只接受絕對 HTTPS；http、相對路徑與其他 scheme 都不可透過。</summary>
    /// <param name="url">刻意不合格的圖示 URI。</param>
    [Theory]
    [InlineData("http://example.invalid/icon.png")]
    [InlineData("/icon.png")]
    [InlineData("file:///icon.png")]
    [InlineData("")]
    public void Sender_NonHttpsIcon_Throws(string url) => Assert.Throws<ArgumentException>(() => new Sender("合成教會", url));

    /// <summary>一及二十字邊界合法，透過真實 SDK 傳輸序列化後 sender 位於訊息內；不更改未設定 sender 的黃金檔。</summary>
    /// <param name="length">合法名稱的兩個邊界。</param><returns>已排空的離線捕獲作業。</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task Sender_Serializes_NameAndIconUrl(int length)
    {
        using var handler = new LineSdkGoldenTests.GoldenHandler();
        using var transport = new System.Net.Http.HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        var sender = new Sender(new string('a', length), "https://example.invalid/icon.png");
        await client.PushMessageAsync("synthetic-recipient", new[] { new TextMessage("合成") { Sender = sender } });
        var json = JObject.Parse(handler.Body)["messages"]![0]!["sender"]!;
        Assert.Equal(sender.Name, (string?)json["name"]);
        Assert.Equal(sender.IconUrl, (string?)json["iconUrl"]);
    }

    /// <summary>漸層完整欄位透過 Flex Box 真正進入請求；沒有背景的既有 Flex 黃金字串保持不變。</summary>
    /// <returns>已釋放 transport 及 client 的捕獲作業。</returns>
    [Fact]
    public async Task Background_LinearGradient_Serializes()
    {
        using var handler = new LineSdkGoldenTests.GoldenHandler();
        using var transport = new System.Net.Http.HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        var background = new Background { Angle = "90deg", StartColor = "#FF0000", CenterColor = "#00FF00", EndColor = "#0000FF", CenterPosition = "25%" };
        var box = new BoxComponent(BoxLayout.Vertical) { Background = background };
        await client.PushMessageAsync("synthetic-recipient", new[] { new FlexMessage("合成") { Contents = new BubbleContainer { Body = box } } });
        var json = JObject.Parse(handler.Body)["messages"]![0]!["contents"]!["body"]!["background"]!;
        Assert.Equal("{\"type\":\"linearGradient\",\"angle\":\"90deg\",\"startColor\":\"#FF0000\",\"centerColor\":\"#00FF00\",\"endColor\":\"#0000FF\",\"centerPosition\":\"25%\"}", json.ToString(Formatting.None));
    }

    /// <summary>LINE 官方事件外形的新增欄位必須由共同解析入口讀取，舊事件缺欄位時維持安全預設。</summary>
    /// <param name="type">可解析的事件類別。</param><param name="extra">該事件既有必填欄位。</param>
    [Theory]
    [InlineData("message", "\"message\":{\"id\":\"synthetic-message\",\"type\":\"text\",\"text\":\"hello\"},")]
    [InlineData("follow", "")]
    [InlineData("unfollow", "")]
    [InlineData("join", "")]
    [InlineData("leave", "")]
    [InlineData("postback", "\"postback\":{\"data\":\"OPEN\"},")]
    public void WebhookEvent_Deserialize_ReadsWebhookEventIdAndRedelivery(string type, string extra)
    {
        var prefix = "{\"events\":[{\"type\":\"" + type + "\",\"source\":{\"type\":\"user\",\"userId\":\"synthetic-user\"},\"timestamp\":1462629479859,\"replyToken\":\"synthetic-reply\"," + extra;
        var current = Assert.Single(WebhookEventParser.Parse(prefix + "\"webhookEventId\":\"01SYNTHETICEVENT\",\"deliveryContext\":{\"isRedelivery\":true}}]}"));
        Assert.Equal("01SYNTHETICEVENT", current.WebhookEventId);
        Assert.True(current.DeliveryContext.IsRedelivery);
        var legacy = Assert.Single(WebhookEventParser.Parse(prefix.TrimEnd(',') + "}]}"));
        Assert.Null(legacy.WebhookEventId);
        Assert.False(legacy.DeliveryContext.IsRedelivery);
    }

    /// <summary>RFC 4231 case 2 的固定結果，不使用產品演算法自行產生 expected；修改任意 digest 位元組均須拒絕。</summary>
    [Fact]
    public void Signature_ValidAndEveryTamperedByte_AreVerified()
    {
        var body = Encoding.UTF8.GetBytes("what do ya want for nothing?");
        var secret = "Jefe";
        const string expected = "W9zBRr9gdU5qBCQmCJV1x1oAPwidJzmDnexYuWTsOEM=";
        Assert.True(LineSignature.Verify(body, expected, secret));
        var digest = Convert.FromBase64String(expected);
        for (var index = 0; index < digest.Length; index++)
        {
            var changed = (byte[])digest.Clone();
            changed[index] ^= 1;
            Assert.False(LineSignature.Verify(body, Convert.ToBase64String(changed), secret));
        }
        Assert.False(LineSignature.Verify(Encoding.UTF8.GetBytes("what do ya want for nothing?\n"), expected, secret));
        Assert.Equal("what do ya want for nothing?", Encoding.UTF8.GetString(body));
    }

    /// <summary>非法 Base64、非 32-byte digest、缺 header 都回 false，不拋出攻擊者可控格式例外。</summary>
    /// <param name="signature">不合格的 header。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("YQ==")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void Signature_InvalidHeader_ReturnsFalse(string? signature) => Assert.False(LineSignature.Verify(new byte[] { 1 }, signature!, "synthetic-secret"));

    /// <summary>空 secret 是主機設定錯誤，必須明確失敗而不是接受以空密鑰計算的簽章。</summary>
    /// <param name="secret">空或只有空白的設定。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Signature_EmptySecret_Throws(string? secret) => Assert.Throws<ArgumentException>(() => LineSignature.Verify(Array.Empty<byte>(), "", secret!));

    /// <summary>兩個 channel 交錯一千次驗證不能共用 HMAC 或密鑰；所有有限作業以 WhenAll 排空，不留背景工作。</summary>
    /// <returns>已等待全部一千次驗證的工作。</returns>
    [Fact]
    public async Task Signature_TwoChannelsParallel_NoSharedState()
    {
        var body = Encoding.UTF8.GetBytes("what do ya want for nothing?");
        var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() => Assert.Equal(index % 2 == 0,
            LineSignature.Verify(body, "W9zBRr9gdU5qBCQmCJV1x1oAPwidJzmDnexYuWTsOEM=", index % 2 == 0 ? "Jefe" : "different-channel")))).ToArray();
        await Task.WhenAll(tasks);
        Assert.All(tasks, task => Assert.True(task.IsCompleted));
    }
}
