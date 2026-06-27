using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// 將 ASP.NET Core 的 <see cref="HttpRequest"/> 轉成金流核心可理解的 provider-neutral callback request。
/// 這個類別刻意留在 ChurchReport，因為 <see cref="HttpRequest"/> 屬於 Web/ASP.NET 邊界，
/// 不應讓可重用的 <c>SpeechMessage.Payments</c> 核心直接依賴 Controller 或 HTTP runtime 型別。
/// </summary>
public sealed class PaymentHttpRequestMapper
{
    /// <summary>
    /// 收集 callback 所需的 method、content type、raw body、query、form 與 headers。
    /// provider-specific 的欄位解析與簽章驗證仍由金流核心各 provider 實作負責，
    /// 這裡只做 HTTP 資料搬運，不解讀永豐、高鉅或台新協定。
    /// </summary>
    public async Task<PaymentCallbackRequest> MapAsync(
        HttpRequest request,
        string profileName,
        PaymentProviderKind? providerHint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rawBody = await ReadRawBodyAsync(request, cancellationToken);
        var form = request.HasFormContentType
            ? Flatten(await request.ReadFormAsync(cancellationToken))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new PaymentCallbackRequest
        {
            ProfileName = profileName,
            ProviderHint = providerHint,
            HttpMethod = request.Method,
            ContentType = request.ContentType ?? string.Empty,
            RawBody = rawBody,
            Query = Flatten(request.Query),
            Form = form,
            Headers = Flatten(request.Headers)
        };
    }

    private static async Task<string> ReadRawBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // Callback 可能已經被 MVC model binding 或其他 middleware 讀過 body。
        // 啟用 buffering 並在讀取前後重設 Position，避免 provider callback parser 拿到空字串。
        request.EnableBuffering();

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        return rawBody;
    }

    private static IReadOnlyDictionary<string, string> Flatten(IEnumerable<KeyValuePair<string, StringValues>> values)
    {
        // ASP.NET 的 query/form/header 允許同名多值；核心 contract 使用單值字典，
        // 因此這裡用逗號串接保留全部值，並以不分大小寫字典避免 provider callback 欄位大小寫差異。
        return values.ToDictionary(
            pair => pair.Key,
            pair => string.Join(",", pair.Value.ToArray()),
            StringComparer.OrdinalIgnoreCase);
    }
}
