/*
檔案整體說明
目的與定位：P3-06 LINE profile 與語音內容的具體 net10 HTTP adapter；修正舊 MessageStore.cs:403–529 無 Bearer、無界下載及 MIME 猜測。
主要功能與流程：只接受已授權 scope，URL 固定官方主機及經驗證的識別。每次建立 request 並設定 Bearer，以 ResponseHeadersRead 先驗 Content-Length 再逐塊讀取上限；audio MIME 嚴格白名單。Profile JSON 上限 64 KiB，語音上限 5 MiB。
使用邊界與維護：HttpClient 由 DI factory 擁有，request／response／stream 由本次 using 唯一擁有；固定上限的 ArrayPool 緩衝在成功、取消與故障時都清零後歸還，輸出的獨立位元組由呼叫端於使用後清零。取消會中止真正 I/O，全部 await 後返回，沒有 Task.Run 或背景工作。共用 header／cookie 不承載身分，正式 registration 禁止 redirect/cookies。失敗及超限不回空 bytes，不寫 CRM；原 Exception 交外層共用告警。離線測試用 HttpMessageHandler，不連 LINE。
*/
using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;

/// <summary>已驗證且有界的 profile 純值，不含 token、HTTP owner 或可變集合。</summary>
public sealed record WebhookProfile(string DisplayName, string PictureUrl, string StatusMessage);

/// <summary>可注入窄 HTTP handler 的真實串流 adapter，不自行擁有 HttpClient。</summary>
public sealed class WebhookHttpAdapter(HttpClient client)
{
    /// <summary>本地音訊記憶體預算，沿用附件 Core 的 5 MiB，不宣稱是 D365 實機上限。</summary>
    public const int MaximumAudioBytes = 5 * 1024 * 1024;
    /// <summary>讀取本人／群組 profile，API 回應識別不一致直接拒絕；不從不受信任 URL 下載。</summary>
    public async Task<WebhookProfile> ReadProfileAsync(ResolvedWebhookScope scope, CancellationToken ct)
    {
        var group = scope.SourceId[0] == 'C';
        if (scope.SourceId[0] == 'R') throw new InvalidOperationException("Room profile semantics require an explicit source contract.");
        var path = group ? "group/" + scope.SourceId + "/summary" : "profile/" + scope.SourceId;
        var bytes = await ReadAsync(scope, new Uri("https://api.line.me/v2/bot/" + path), 65536, false, ct).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var idName = group ? "groupId" : "userId";
            if (!root.TryGetProperty(idName, out var id) || id.GetString() != scope.SourceId) throw new InvalidOperationException("Unexpected LINE profile identity.");
            return new WebhookProfile(ReadString(root, group ? "groupName" : "displayName", 256, true), ReadString(root, "pictureUrl", 2048, false), ReadString(root, "statusMessage", 500, false));
        }
        finally { Array.Clear(bytes, 0, bytes.Length); }
    }
    /// <summary>數字 message ID 只拼入固定官方 URL；每筆以本頻道 token 下載，外部 contentProvider 不使用。</summary>
    public Task<byte[]> DownloadAudioAsync(ResolvedWebhookScope scope, string messageId, CancellationToken ct)
    {
        if (messageId is not { Length: > 0 and <= 64 } || messageId.Any(c => !char.IsAsciiDigit(c))) throw new ArgumentException("Invalid message identity.", nameof(messageId));
        return ReadAsync(scope, new Uri("https://api-data.line.me/v2/bot/message/" + messageId + "/content"), MaximumAudioBytes, true, ct);
    }
    /// <summary>雙重大小限制包含無 Content-Length 與偽造短長度；只完整成功才發布 bytes。</summary>
    private async Task<byte[]> ReadAsync(ResolvedWebhookScope scope, Uri uri, int maximum, bool audio, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scope.AccessToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidOperationException("LINE content exceeds its budget.");
        var mime = response.Content.Headers.ContentType?.MediaType;
        if (audio && mime is not ("audio/x-m4a" or "audio/mp4" or "audio/m4a")) throw new InvalidOperationException("Unsupported audio MIME type.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(checked(maximum + 1));
        try
        {
            var length = 0;
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(length, Math.Min(8192, maximum + 1 - length)), ct).ConfigureAwait(false);
                if (count == 0) break;
                length += count;
                if (length > maximum) throw new InvalidOperationException("LINE content exceeds its budget.");
            }
            ct.ThrowIfCancellationRequested();
            var result = new byte[length];
            Buffer.BlockCopy(buffer, 0, result, 0, length);
            return result;
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    /// <summary>JSON 欄位採嚴格型別及長度，不能將異常 profile 靜默寫成空白成功。</summary>
    private static string ReadString(JsonElement root, string name, int maximum, bool required)
    {
        if (!root.TryGetProperty(name, out var value)) return required ? throw new InvalidOperationException("Incomplete LINE profile.") : string.Empty;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length > maximum || (required && string.IsNullOrWhiteSpace(text)))
            throw new InvalidOperationException("Invalid LINE profile field.");
        return text;
    }
}
