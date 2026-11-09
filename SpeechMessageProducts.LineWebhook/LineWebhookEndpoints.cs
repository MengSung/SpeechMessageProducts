/*
檔案整體說明
目的與定位：階段三 LINE Webhook 的有界 HTTP adapter，只依驗簽、去重及 queue 三個必要業務介面，不內建假正式服務。
主要功能與流程：先限制一 MiB 原始 body，再驗同一 bytes、解析完整候選及檢查穩定 ID，最後逐事件 claim／queue；成功 200、驗簽拒絕 401、超量 413、真正基礎設施故障或滿載 503。舊路由預設不映射，啟用時固定使用部署指定 channel。
使用邊界與維護：route channel 不是授權，只有 verifier 能在伺服器部署映射後確認範圍，destination 不是數字 channel。raw body 使用最多二 MiB 的 pooled 緩衝且僅目前 request 擁有，驗簽／解析後立即清零歸還；JsonDocument 確定 Dispose，queue 只接 detached 不可變單筆事件。registration 由 endpoint 唯一 Dispose，拒絕或失敗 rollback，正常 caller 取消不通知，其他例外傳同一原物件到 Hosting，不輸出 body／標頭／秘密。
*/
using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using LineMessagingProcessor.AspNetCore.Webhooks;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.LineWebhook;

/// <summary>只處理 HTTP 轉接與有界生命週期，不自行持有 credential、SQL connection 或背景工作。</summary>
public static class LineWebhookEndpoints
{
    /// <summary>只接受 POST；legacy 開關是啟動快照，query／body 不能改寫其指定頻道。</summary>
    /// <param name="app">已完成共用註冊的主機，呼叫端仍擁有其啟動與關閉。</param>
    /// <returns>同一主機，未完成三項正式依賴時仍由 startup 拒絕。</returns>
    public static WebApplication MapLineWebhook(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost("/api/line/webhook/{channelId}", (Func<HttpContext, Task<IResult>>)(context =>
            HandleAsync(context, context.Request.RouteValues["channelId"] as string)));
        var options = app.Services.GetRequiredService<WebhookOptions>();
        if (options.EnableLegacyCallback)
            app.MapPost("/api/callback", (Func<HttpContext, Task<IResult>>)(context => HandleAsync(context, options.LegacyChannelId)));
        return app;
    }

    /// <summary>執行有限 body → 驗簽 → 完整解析 → 每事件 claim／handoff，所有資源在返回任何狀態前清理。</summary>
    private static async Task<IResult> HandleAsync(HttpContext context, string? channelId)
    {
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
