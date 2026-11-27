/*
檔案整體說明
目的與定位：F3 的離線可驗證健康檢查服務，僅接受固定產品目錄已驗證且可服務的 Channel，不接受任意寫入 target 或網址。
主要功能與流程：以逐訊息 Bearer 呼叫 bot/info、月用量與 richmenu/list，成功才回寫統計及 IChurchClock UTC；401 停用該正式資料列、失效目錄並告警一次。
使用邊界與維護：借用 HttpClient，不修改 DefaultHeaders、不 Dispose 外部 client；HTTP 訊息、回應與串流由單次十秒操作 owner 確定釋放。正常 caller 取消不告警，內部逾時及真故障保留原 Exception 交共用診斷；本檔不註冊排程、不實作 F5 推播。
*/
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace ToolUtilityNameSpace.Channels;

/// <summary>只檢查目錄內可服務頻道；借入 client 必須無 Cookie、無自動轉址且不保存認證狀態。</summary>
public sealed class ChannelHealthChecker
{
    private const int MaximumResponseBytes = 64 * 1024;
    private readonly DataverseChannelDirectory directory;
    private readonly HttpClient httpClient;
    /// <summary>借入固定目錄與無使用者狀態的 client；關閉順序為 checker 操作排空、directory 排空、最後 client。</summary>
    public ChannelHealthChecker(DataverseChannelDirectory directory, HttpClient httpClient)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>只以已載入正式 Channel 呼叫固定 LINE API；成功完整後才寫回 owner 已解析資料列。</summary>
    public async Task<ChannelHealthResult> CheckAsync(string channelKey, CancellationToken cancellationToken)
    {
        using var operation = directory.Begin(cancellationToken);
        DataverseChannelDirectory.Entry? entry = null;
        try
        {
            entry = await directory.ResolveAsync(channelKey, operation.Token).ConfigureAwait(false);
            if (entry == null) throw new KeyNotFoundException("Channel is not available in the fixed product directory.");
            var totalUsage = 0;
            var richMenuCount = 0;
            foreach (var path in new[] { "/v2/bot/info", "/v2/bot/message/quota/consumption", "/v2/bot/richmenu/list" })
            {
                using var request = CreateRequest(path, entry.Token);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, operation.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var failed = new ChannelHealthResult(false, 0, 0, directory.UtcNow);
                    await directory.DisableAsync(entry, failed, operation).ConfigureAwait(false);
                    return failed;
                }
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(operation.Token).ConfigureAwait(false);
                using var document = await ReadJsonAsync(stream, operation.Token).ConfigureAwait(false);
                if (path == "/v2/bot/message/quota/consumption")
                    totalUsage = ReadNonnegativeInt(document.RootElement, "totalUsage");
                else if (path == "/v2/bot/richmenu/list")
                    richMenuCount = ReadArrayCount(document.RootElement, "richmenus");
                else
                    ValidateBotInfo(document.RootElement);
            }
            var result = new ChannelHealthResult(true, totalUsage, richMenuCount, directory.UtcNow);
            await directory.WriteHealthAsync(entry, result, operation.Token).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            directory.Failure(exception, "ChannelHealth.Check", operation);
            throw;
        }
    }

    /// <summary>每次建立固定 HTTPS request 並逐訊息設定 Authorization，絕不修改 HttpClient.DefaultRequestHeaders。</summary>
    private static HttpRequestMessage CreateRequest(string path, string token)
    {
        if (path is not ("/v2/bot/info" or "/v2/bot/message/quota/consumption" or "/v2/bot/richmenu/list"))
            throw new InvalidOperationException("Health endpoint is not allowlisted.");
        return new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.line.me" + path, UriKind.Absolute))
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
    }

    /// <summary>完整驗 bot/info 的必要非空 userId，不把識別值保留於結果或診斷。</summary>
    private static void ValidateBotInfo(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("userId", out var userId)
            || userId.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(userId.GetString()))
            throw new InvalidDataException("LINE bot info response is invalid.");
    }

    /// <summary>只讀非負 Int32 用量，拒絕缺漏、負數及超界值，不把服務端本文放進例外。</summary>
    private static int ReadNonnegativeInt(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value)
            || !value.TryGetInt32(out var result) || result < 0)
            throw new InvalidDataException("LINE health usage response is invalid.");
        return result;
    }

    /// <summary>完整驗 richmenu/list 是陣列且不超 LINE 上限；回傳數量不需要保存外部 ID。</summary>
    private static int ReadArrayCount(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("LINE health list response is invalid.");
        var count = 0;
        foreach (var _ in value.EnumerateArray())
            if (++count > 1000) throw new InvalidDataException("LINE health list exceeds its bound.");
        return count;
    }

    /// <summary>最多緩衝 64 KiB；stream 單一 owner 讀到底或超量失敗，拒絕固定前綴假裝完整 JSON。</summary>
    private static async Task<JsonDocument> ReadJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaximumResponseBytes) throw new InvalidDataException("LINE health response exceeds its bound.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
    }

}
