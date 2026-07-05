// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class PaymentHttpRequestMapper
// 主要成員：MapAsync、ReadRawBodyAsync
// 引用命名空間：System、System.Collections.Generic、System.IO、System.Linq、System.Threading、System.Threading.Tasks、Microsoft.AspNetCore.Http、Microsoft.Extensions.Primitives
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.AspNetCore;

/// <summary>
/// Maps ASP.NET Core <see cref="HttpRequest"/> data into the provider-neutral
/// <see cref="PaymentCallbackRequest"/> consumed by <c>SpeechMessage.Payments</c>.
/// This type is host glue: it belongs in the ASP.NET integration project, not in
/// the provider core and not in any single product application.
/// </summary>
public sealed class PaymentHttpRequestMapper
{
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
        return values.ToDictionary(
            pair => pair.Key,
            pair => string.Join(",", pair.Value.ToArray()),
            StringComparer.OrdinalIgnoreCase);
    }
}
