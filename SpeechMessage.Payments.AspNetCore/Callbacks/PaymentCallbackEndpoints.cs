/*
 * 檔案整體說明
 *
 * 目的與定位：提供 QPay ATM JSON 與信用卡 form-urlencoded 的 ASP.NET Core HTTP adapter，只有 Payment:Callbacks:Enabled 明確為 true 時才映射指定 POST 路徑。
 * 主要流程：先拒絕已知超長 Content-Length，再以固定 64 KiB+1 request-local 緩衝偵測 chunked 超限；嚴格解析 UTF-8、JSON／表單及重複欄位，只向 Processor 傳遞 ShopNo／PayToken 的正規化投影。
 * 使用邊界與維護：request body 不交給無界 ReadFormAsync、自動模型繫結或 PaymentHttpRequestMapper，也不保留於 callback DTO、診斷或 HTTP response。每個緩衝僅屬單一 request 且大小有界；caller disconnect 依 RequestAborted 結束，不記為例外通知。固定回應不包含 provider 文字、token、金鑰或個資；由 PaymentCallbackHostTests 驗證路由開關、大小限制與安全結果。
 */
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SpeechMessage.Payments.Models;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Payments.AspNetCore.Callbacks;

/// <summary>
/// 將 QPay ATM 與信用卡回呼映射至共用 processor；本類別只負責有限輸入解析及固定 HTTP 回應。
/// </summary>
public static class PaymentCallbackEndpoints
{
    private const int MaximumBodyBytes = 64 * 1024;
    private const int MaximumReadBytes = MaximumBodyBytes + 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// 僅在 `Payment:Callbacks:Enabled` 明確為 true 時映射兩個指定 POST endpoint；不搬移舊 ATM GET 路徑。
    /// </summary>
    /// <param name="app">由 Host 擁有的 WebApplication。</param>
    /// <returns>原 WebApplication，供組合根延續 route mapping。</returns>
    /// <exception cref="ArgumentNullException">Host 為 null。</exception>
    /// <exception cref="InvalidOperationException">開關存在但不是合法 Boolean。</exception>
    public static WebApplication MapSpeechMessagePaymentCallbacks(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var enabledValue = app.Configuration["Payment:Callbacks:Enabled"];
        if (enabledValue is null)
        {
            return app;
        }

        if (!bool.TryParse(enabledValue, out var enabled))
        {
            throw new InvalidOperationException("Payment:Callbacks:Enabled must be a Boolean.");
        }

        if (!enabled)
        {
            return app;
        }

        app.MapPost(
            "/api/QPayAtm/QPayBackendUrl",
            (HttpContext context, PaymentCallbackProcessor processor, ExceptionDiagnostics diagnostics) =>
                HandleAsync(context, processor, diagnostics, CallbackBodyKind.Json));
        app.MapPost(
            "/api/QPayCard/QPayReturnUrl",
            (HttpContext context, PaymentCallbackProcessor processor, ExceptionDiagnostics diagnostics) =>
                HandleAsync(context, processor, diagnostics, CallbackBodyKind.Form));
        return app;
    }

    /// <summary>讀取並驗證單一回呼本文，將 processor outcome 轉成兩端點共用的固定安全回應形狀。</summary>
    private static async Task HandleAsync(
        HttpContext context,
        PaymentCallbackProcessor processor,
        ExceptionDiagnostics diagnostics,
        CallbackBodyKind bodyKind)
    {
        PaymentCallbackRequest request;
        try
        {
            var body = await ReadBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            if (body is null)
            {
                await WriteFailureAsync(context, StatusCodes.Status413RequestEntityTooLarge).ConfigureAwait(false);
                return;
            }

            if (!TryCreateCallbackRequest(context.Request, body, bodyKind, out request))
            {
                await WriteFailureAsync(context, StatusCodes.Status400BadRequest).ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Report(exception, "PaymentCallbackEndpoints.ReadRequest", context.RequestAborted);
            await WriteFailureAsync(context, StatusCodes.Status503ServiceUnavailable).ConfigureAwait(false);
            return;
        }

        PaymentCallbackOutcome outcome;
        try
        {
            outcome = await processor.ProcessAsync(request, context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Report(exception, "PaymentCallbackEndpoints.ProcessRequest", context.RequestAborted);
            await WriteFailureAsync(context, StatusCodes.Status503ServiceUnavailable).ConfigureAwait(false);
            return;
        }

        if (outcome == PaymentCallbackOutcome.Accepted)
        {
            await WriteSuccessAsync(context, bodyKind).ConfigureAwait(false);
            return;
        }

        var statusCode = outcome switch
        {
            PaymentCallbackOutcome.BadRequest => StatusCodes.Status400BadRequest,
            PaymentCallbackOutcome.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        await WriteFailureAsync(context, statusCode).ConfigureAwait(false);
    }

    /// <summary>以 request-local 串流緩衝最多讀取 64 KiB+1；不依未受限外部長度配置記憶體。</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is > MaximumBodyBytes)
        {
            return null;
        }

        var initialCapacity = request.ContentLength is >= 0 and <= MaximumBodyBytes
            ? (int)request.ContentLength.Value
            : 0;
        using var body = new MemoryStream(initialCapacity);
        var readBuffer = new byte[8192];
        while (body.Length < MaximumReadBytes)
        {
            var read = await request.Body.ReadAsync(
                    readBuffer.AsMemory(0, (int)Math.Min(readBuffer.Length, MaximumReadBytes - body.Length)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            body.Write(readBuffer, 0, read);
        }

        if (body.Length > MaximumBodyBytes)
        {
            return null;
        }

        return body.ToArray();
    }

    /// <summary>依 endpoint 固定格式解析本文；任何 UTF-8、JSON、表單或必要欄位歧義均 fail closed。</summary>
    private static bool TryCreateCallbackRequest(
        HttpRequest request,
        byte[] body,
        CallbackBodyKind bodyKind,
        out PaymentCallbackRequest callbackRequest)
    {
        callbackRequest = new PaymentCallbackRequest();
        if (!HasExpectedContentType(request.ContentType, bodyKind))
        {
            return false;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (!TryReadFields(text, bodyKind, out var shopNo, out var payToken))
        {
            return false;
        }

        callbackRequest = new PaymentCallbackRequest
        {
            HttpMethod = request.Method,
            ContentType = request.ContentType ?? string.Empty,
            Form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ShopNo"] = shopNo,
                ["PayToken"] = payToken
            }
        };
        return true;
    }

    /// <summary>限制 media type 與 endpoint 格式相符；charset 參數由 strict UTF-8 decoder 一致處理。</summary>
    private static bool HasExpectedContentType(string? contentType, CallbackBodyKind bodyKind)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return false;
        }

        var expected = bodyKind == CallbackBodyKind.Json
            ? "application/json"
            : "application/x-www-form-urlencoded";
        return string.Equals(parsed.MediaType.Value, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>只投影 ShopNo 與 PayToken；ATM JSON 對大小寫不同的重複名稱也拒絕。</summary>
    private static bool TryReadFields(
        string body,
        CallbackBodyKind bodyKind,
        out string shopNo,
        out string payToken)
    {
        shopNo = string.Empty;
        payToken = string.Empty;

        try
        {
            if (bodyKind == CallbackBodyKind.Json)
            {
                using var document = JsonDocument.Parse(
                    body,
                    new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        return false;
                    }

                    if (property.Name.Equals("ShopNo", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return false;
                        }

                        shopNo = property.Value.GetString() ?? string.Empty;
                    }
                    else if (property.Name.Equals("PayToken", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return false;
                        }

                        payToken = property.Value.GetString() ?? string.Empty;
                    }
                }
            }
            else
            {
                var fields = QueryHelpers.ParseQuery(body);
                if (!TryGetSingleField(fields, "ShopNo", out shopNo) ||
                    !TryGetSingleField(fields, "PayToken", out payToken))
                {
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        shopNo = shopNo.Trim();
        payToken = payToken.Trim();
        return IsSafeRequiredValue(shopNo) && IsSafeRequiredValue(payToken);
    }

    /// <summary>從表單多值集合只接受恰好一個必要欄位值，拒絕重複 key 以免 parser 間解讀不一致。</summary>
    private static bool TryGetSingleField(
        IDictionary<string, Microsoft.Extensions.Primitives.StringValues> fields,
        string name,
        out string value)
    {
        if (fields.TryGetValue(name, out var values) && values.Count == 1)
        {
            value = values[0] ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>必要欄位不得為空白或含控制字元；不在 HTTP adapter 保留或回傳其實際值。</summary>
    private static bool IsSafeRequiredValue(string value) =>
        value.Length > 0 && !value.Any(char.IsControl);

    /// <summary>回傳固定錯誤本文，絕不回傳 provider 文字、例外訊息或原始 callback。</summary>
    private static async Task WriteFailureAsync(HttpContext context, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        var body = statusCode switch
        {
            StatusCodes.Status400BadRequest => "Invalid callback.",
            StatusCodes.Status409Conflict => "Callback rejected.",
            StatusCodes.Status413RequestEntityTooLarge => "Callback body too large.",
            _ => "Callback temporarily unavailable."
        };
        await context.Response.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>兩個既定 QPay endpoint 的成功 acknowledgement；本文內容固定且不含交易資料。</summary>
    private static async Task WriteSuccessAsync(HttpContext context, CallbackBodyKind bodyKind)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        if (bodyKind == CallbackBodyKind.Json)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"Status\":\"S\"}", context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("OK", context.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <summary>endpoint 固定輸入格式，避免依 client Content-Type 自動選擇 parser。</summary>
    private enum CallbackBodyKind
    {
        Json,
        Form
    }
}
