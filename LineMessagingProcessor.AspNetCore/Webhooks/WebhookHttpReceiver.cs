/*
檔案整體說明
目的與定位：LINE ASP.NET Core 共用 HTTP 接收器，供既有 LineWebhook 與三產品主機透過各自固定路由委派，不參考任何產品組合根。
主要功能與流程：限制一 MiB 原始本文、以同一份 bytes 驗證 Channel 簽章，再完整解析並驗證穩定事件 ID，最後透過共用去重及有界 queue 逐筆移交。驗簽拒絕回 401、過量 413、無效事件 400，真正故障或滿載回 503。
使用邊界與維護：路由不是授權，Channel 必須由伺服器 resolver 驗證；request 擁有的 pooled body 在驗簽及解析後清零歸還，任何故障亦經 finally 釋放。JsonDocument 與每筆 claim 均確定清理；queue 僅接不可變 detached 事件，不持有 HttpContext、credential 或租用陣列。所有 I/O 尊重 caller 取消，真正失敗傳原始例外交給注入的共用診斷；保留既有固定 operation 符號以維持追蹤相容，不輸出本文、簽章或使用者識別。既有主機回歸與三產品真 Kestrel 合成測試驗證此共用邊界，離線結果不代表實機通過。
*/
using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>只負責 HTTP 轉接；驗簽、去重與 queue 的資源仍由目前 Host 的 DI owner 擁有。</summary>
public static class WebhookHttpReceiver
{
    /// <summary>執行有限 body → 驗簽 → 完整解析 → 每事件 claim／handoff，所有資源在返回任何狀態前清理。</summary>
    /// <param name="context">目前 HTTP request 的借用 context；不得保存到 singleton、queue 或背景工作。</param>
    /// <param name="channelId">尚未信任的路由 Channel，必須經伺服器端 resolver 與原始本文驗簽後才能使用。</param>
    /// <returns>驗簽、完整解析及入列結果；任何返回路徑都釋放本次 body 與 claim。</returns>
    public static async Task<IResult> ReceiveAsync(HttpContext context, string? channelId)
    {
        ArgumentNullException.ThrowIfNull(context);
        var caller = context.RequestAborted;
        var diagnostics = context.RequestServices.GetRequiredService<ExceptionDiagnostics>();
        byte[]? body = null;
        try
        {
            if (context.Request.ContentLength > WebhookOptions.MaximumBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            // Kestrel 的 chunked 傳輸上限會計入 framing；不能把其 wire limit 直接設成 JSON byte 上限，
            // 否則恰好一 MiB 的合法 chunked body 會誤回 413。保留部署的 transport 限制，
            // 此處再對解碼後的原始 body 獨立讀取最多一 MiB 加一個偵測 byte。
            body = ArrayPool<byte>.Shared.Rent(WebhookOptions.MaximumBodyBytes + 1);
            var count = 0;
            while (count <= WebhookOptions.MaximumBodyBytes)
            {
                var read = await context.Request.Body.ReadAsync(
                    body.AsMemory(count, WebhookOptions.MaximumBodyBytes + 1 - count), caller).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            if (count > WebhookOptions.MaximumBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var header = context.Request.Headers["X-Line-Signature"];
            if (!WebhookOptions.IsValidChannelId(channelId) || header.Count != 1 ||
                header[0] is not { Length: > 0 and <= 256 } signature || signature.Contains(',', StringComparison.Ordinal))
                return Results.Unauthorized();
            var verifier = context.RequestServices.GetRequiredService<IWebhookSignatureVerifier>();
            if (!await verifier.VerifyAsync(channelId!, body.AsMemory(0, count), signature, caller).ConfigureAwait(false))
                return Results.Unauthorized();
            List<WebhookEnvelope> events;
            try { events = ParseEvents(channelId!, body.AsMemory(0, count)); }
            catch (JsonException) { return Results.BadRequest(); }
            catch (ArgumentException) { return Results.BadRequest(); }
            ReturnBody(ref body);
            var deduplicator = context.RequestServices.GetRequiredService<IWebhookEventDeduplicator>();
            var queue = context.RequestServices.GetRequiredService<IWebhookEventQueue>();
            foreach (var envelope in events)
            {
                caller.ThrowIfCancellationRequested();
                var registration = await deduplicator.TryRegisterAsync(envelope, caller).ConfigureAwait(false);
                if (registration is null) continue;
                if (!await HandoffAsync(queue, envelope, registration, diagnostics, caller).ConfigureAwait(false))
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            return Results.Ok();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode is 400 or 413)
        {
            return Results.StatusCode(exception.StatusCode);
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (Exception exception)
        {
            diagnostics.Report(exception, "LineWebhookEndpoints.Receive");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        finally { ReturnBody(ref body); }
    }

    /// <summary>endpoint 唯一擁有 claim；主故障及清理故障分別上報同一原物件，finally 不得遮蔽主故障或漏 rollback。</summary>
    private static async Task<bool> HandoffAsync(
        IWebhookEventQueue queue, WebhookEnvelope envelope, IWebhookEventRegistration registration,
        ExceptionDiagnostics diagnostics, CancellationToken caller)
    {
        var accepted = false;
        try
        {
            accepted = await queue.TryEnqueueAsync(envelope, registration, caller).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Report(exception, "LineWebhookEndpoints.Handoff");
        }
        finally
        {
            try { await registration.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception)
            {
                // 清理以自己的有界 token 執行；即使 HTTP 已取消，rollback 故障仍是真失敗。
                diagnostics.Report(exception, "LineWebhookEndpoints.Release");
                accepted = false;
            }
        }
        return accepted;
    }

    /// <summary>只在驗簽後解析，限制深度與一千事件，整批重複穩定 ID 或模糊欄位拒絕，尚未進行 SQL／queue。</summary>
    private static List<WebhookEnvelope> ParseEvents(string channelId, ReadOnlyMemory<byte> body)
    {
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("events", out var events) ||
            events.ValueKind != JsonValueKind.Array || events.GetArrayLength() > 1000)
            throw new JsonException("Invalid webhook event collection.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name)) throw new JsonException("Ambiguous webhook collection.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var candidate = new List<WebhookEnvelope>(events.GetArrayLength());
        foreach (var item in events.EnumerateArray())
        {
            var envelope = new WebhookEnvelope(channelId, item);
            if (!identities.Add(envelope.WebhookEventId)) throw new JsonException("Conflicting webhook identities.");
            candidate.Add(envelope);
        }
        return candidate;
    }

    /// <summary>將原始 body 全緩衝清零後歸還 pool，重複呼叫安全；不可把租用陣列或其 memory 保存至 queue。</summary>
    private static void ReturnBody(ref byte[]? body)
    {
        if (body is null) return;
        CryptographicOperations.ZeroMemory(body);
        ArrayPool<byte>.Shared.Return(body);
        body = null;
    }
}
