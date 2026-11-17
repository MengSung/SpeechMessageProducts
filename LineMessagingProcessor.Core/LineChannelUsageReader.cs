/*
檔案整體說明
目的與定位：LINE 共用核心的 net10 有界 Channel 用量查詢 owner，供部署演練等工具使用，
避免上層自行持有 HttpClient。既有 SDK consumption 入口沒有 caller cancellation 及硬回應
容量契約，本入口明確提供這兩項限制，不改變 SDK 相容 API。
主要流程：只 GET 固定 consumption 端點，每個 request 自有認證；回應最多4096 bytes、
15秒總期限，完整 JSON 成功才返回非負整數，不把無資料或失敗當作零。
隔離與生命週期：HttpClient／handler 唯此 owner 擁有，cookie／redirect 關閉，沒有
DefaultRequestHeaders token 或跨 Channel mutable state；token 只存活單次同步呼叫。
request、response、stream、deadline 均 using 排空再釋放，Dispose 關閉本 owner 連線池。
失敗與驗證：固定錯誤不含HTTP本文、token或Channel資料，原HTTP例外向 caller 告警入口傳遞；
合成 handler 驗狀態、容量、取消及 A/B 認證隔離，不把離線查詢描述為 LINE 實機證據。
*/
#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
namespace LineMessagingProcessor.Core;

/// <summary>net10 工具的有限生命週期用量查詢器，無背景工作或 token 快取。</summary>
public sealed class LineChannelUsageReader : IDisposable
{
    private readonly HttpClient http;
    /// <summary>建立本命令獨占傳輸，禁止 cookies 與 redirect，單請求15秒。</summary>
    public LineChannelUsageReader() : this(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { }
    /// <summary>測試注入 handler 亦交此 owner 擁有，Dispose 恰好一次。</summary>
    internal LineChannelUsageReader(HttpMessageHandler handler) => http = new(handler, true) { Timeout = TimeSpan.FromSeconds(15) };
    /// <summary>讀取當次 Channel 已用訊息數；不保存 token、不允任意 URL，錯誤不回零。</summary>
    /// <param name="channelToken">caller 由自己的已授權秘密來源借入，絕不寫入欄位或共用header。</param>
    /// <param name="token">caller 取消，另有15秒內部總期限。</param>
    /// <returns>完整且非負的用量值。</returns>
    public long Read(string channelToken, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(channelToken)) throw new ArgumentException("Channel token is required.", nameof(channelToken));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.line.me/v2/bot/message/quota/consumption");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", channelToken);
        using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new InvalidDataException("LINE usage response is unavailable or invalid.");
        using var stream = response.Content.ReadAsStream(deadline.Token);
        using var body = new MemoryStream();
        var bytes = new byte[1024];
        while (true)
        {
            var count = stream.ReadAsync(bytes.AsMemory(), deadline.Token).AsTask().GetAwaiter().GetResult();
            if (count == 0) break;
            if (body.Length + count > 4096) throw new InvalidDataException("LINE usage response is unavailable or invalid.");
            body.Write(bytes, 0, count);
        }
        using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false });
        return json.RootElement.TryGetProperty("totalUsage", out var usage) && usage.TryGetInt64(out var value) && value >= 0 ? value : throw new InvalidDataException("LINE usage response is unavailable or invalid.");
    }
    /// <summary>所有同步呼叫排空後由 caller 關閉，不釋放其他 Channel owner 的傳輸。</summary>
    public void Dispose() => http.Dispose();
}
#endif
