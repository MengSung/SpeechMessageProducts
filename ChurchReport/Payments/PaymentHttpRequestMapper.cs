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
