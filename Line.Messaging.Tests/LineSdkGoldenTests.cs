using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 以舊版 net10 SDK 實際 HTTP 請求鎖住六類 JSON；樣本只含合成文字及 example.invalid。
/// 比對包含欄位順序與逸出字元的完整字串，不能以解析後相等掩蓋相容性變更。
/// </summary>
public sealed class LineSdkGoldenTests
{
    /// <summary>逐類送入記憶體 handler；不連網，並將 SDK 原始請求與固定 golden 逐字比較。</summary>
    /// <param name="kind">六類既有訊息或 RichMenu 建立請求。</param>
    /// <returns>完成單次捕獲並釋放客戶端的工作。</returns>
    [Theory]
    [InlineData("Text")]
    [InlineData("Flex")]
    [InlineData("Template")]
    [InlineData("Image")]
    [InlineData("QuickReply")]
    [InlineData("RichMenu")]
    public async Task Serialize_AllMessageTypes_MatchGolden(string kind)
    {
        using var handler = new GoldenHandler();
        using var transport = new HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        if (kind == "RichMenu")
        {
            await client.CreateRichMenuAsync(new RichMenu
            {
                Size = new ImagemapSize(2500, 843),
                Selected = true,
                Name = "合成選單",
                ChatBarText = "開啟",
                Areas = new List<ActionArea>
                {
                    new() { Bounds = new ImagemapArea(0, 0, 1250, 843), Action = new MessageTemplateAction("合成操作", "OPEN") }
                }
            });
        }
        else
        {
            await client.PushMessageAsync("synthetic-recipient", new List<ISendMessage> { CreateMessage(kind) });
        }

        using var stream = typeof(LineSdkGoldenTests).Assembly.GetManifestResourceStream("Line.Messaging.Tests.Golden." + kind + ".json")
            ?? throw new InvalidOperationException("缺少固定黃金資源。");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        Assert.Equal(await reader.ReadToEndAsync(), handler.Body);
    }

    /// <summary>建立每次操作獨享的模型，不共用可變清單或任何收件者狀態。</summary>
    /// <param name="kind">合成訊息類別。</param><returns>只屬於此次測試的訊息圖。</returns>
    internal static ISendMessage CreateMessage(string kind) => kind switch
    {
        "Text" => new TextMessage("合成文字\n\"quote\" \\ 😀"),
        "Flex" => new FlexMessage("合成卡片")
        {
            Contents = new BubbleContainer
            {
                Body = new BoxComponent(BoxLayout.Vertical)
                {
                    Contents = new List<IFlexComponent> { new TextComponent("合成標題") { Weight = Weight.Bold, Wrap = true } }
                }
            }
        },
        "Template" => new TemplateMessage("合成確認", new ConfirmTemplate("確認？", new List<ITemplateAction>
        {
            new MessageTemplateAction("是", "YES"), new MessageTemplateAction("否", "NO")
        })),
        "Image" => new ImageMessage("https://example.invalid/full.png", "https://example.invalid/preview.png"),
        "QuickReply" => new TextMessage("合成選項", new QuickReply(new List<QuickReplyButtonObject>
        {
            new(new MessageTemplateAction("選擇", "SELECT"), "https://example.invalid/icon.png")
        })),
        _ => throw new ArgumentException("未知合成訊息類別。", nameof(kind))
    };

    /// <summary>只捕獲單筆請求文字；由測試 Dispose，無 socket、queue、timer 或背景工作。</summary>
    internal sealed class GoldenHandler : HttpMessageHandler
    {
        /// <summary>本次請求的原始序列化文字；只保留最後一筆，測試結束即可回收。</summary>
        internal string Body { get; private set; } = string.Empty;

        /// <summary>同步複製請求文字後回傳合成成功；回應所有權交還 SDK／HttpClient。</summary>
        /// <param name="request">由 SDK 建立的借用請求，不保留物件。</param>
        /// <param name="cancellationToken">HttpClient 的取消界線。</param><returns>合成回應。</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"richMenuId\":\"synthetic-menu\"}", Encoding.UTF8, "application/json")
            };
        }
    }
}
