/*
 * 檔案整體說明
 *
 * 目的與定位：將ASP.NET Core的query、form或原始body整理為共用金流核心的PaymentCallbackRequest；只處理HTTP輸入形狀及串流位置，不在此判定銀行簽章或付款成功。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentHttpRequestMapper。
 * 主要操作入口包含 MapAsync、ReadRawBodyAsync、Flatten。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.AspNetCore/DependencyInjection/PaymentAspNetCoreServiceCollectionExtensions.cs
 * 追查目前工作樹中的合作／使用位置。
 */
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
