using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>
/// 永豐 QPay 狀態正規化。
/// 永豐回應可能同時有 API 層狀態與交易層狀態，產品層只應消費 normalized PaymentStatus。
/// </summary>
internal static class SinopacStatusMapper
{
    private static readonly HashSet<string> SuccessCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "S",
        "SUCCESS",
        "OK",
        "0000",
        "S0000",
        "S00000"
    };

    private static readonly HashSet<string> FailureCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "F",
        "FAIL",
        "FAILED",
        "ERROR",
        "DECLINED"
    };

    public static PaymentStatus MapCreate(SinopacOrderCreateResponse? response)
    {
        if (response is null)
        {
            return PaymentStatus.Unknown;
        }

        if (IsFailureStatus(response.Status))
        {
            return PaymentStatus.Failed;
        }

        return IsSuccessStatus(response.Status) ||
            IsSuccessStatus(response.Description) ||
            ContainsSuccessText(response.Description)
                // 建單成功代表拿到付款頁或付款指示，尚未代表使用者已付款。
                ? PaymentStatus.Pending
                : PaymentStatus.Unknown;
    }

    public static PaymentStatus Map(SinopacOrderPayResponse? response)
    {
        if (response is null)
        {
            return PaymentStatus.Unknown;
        }

        if (IsFailureStatus(response.Status))
        {
            return PaymentStatus.Failed;
        }

        var apiSuccess = IsSuccessStatus(response.Status) ||
            IsSuccessStatus(response.Description) ||
            ContainsSuccessText(response.Description);

        if (!apiSuccess)
        {
            return PaymentStatus.Unknown;
        }

        var transaction = response.TSResultContent;
        if (transaction is null)
        {
            // 查詢 API 成功但沒有交易明細時，保守視為等待中。
            return PaymentStatus.Pending;
        }

        var transactionStatus = transaction.Status;
        var transactionDescription = transaction.Description;

        if (IsPendingStatus(transactionStatus))
        {
            return PaymentStatus.Pending;
        }

        if (IsFailureStatus(transactionStatus) || ContainsFailureText(transactionDescription))
        {
            return PaymentStatus.Failed;
        }

        return IsSuccessStatus(transactionStatus) ||
            IsSuccessStatus(transactionDescription) ||
            ContainsSuccessText(transactionDescription)
                ? PaymentStatus.Succeeded
                : PaymentStatus.Unknown;
    }

    public static bool IsProviderRejected(SinopacOrderPayResponse? response)
    {
        return Map(response) == PaymentStatus.Failed ||
            IsFailureStatus(response?.Status) ||
            IsFailureStatus(response?.TSResultContent?.Status) ||
            ContainsFailureText(response?.TSResultContent?.Description);
    }

    public static bool IsProviderRejected(SinopacOrderCreateResponse? response)
    {
        return MapCreate(response) == PaymentStatus.Failed ||
            IsFailureStatus(response?.Status);
    }

    private static bool IsSuccessStatus(string? value)
    {
        return SuccessCodes.Contains(ExtractLeadingCode(value));
    }

    private static bool IsFailureStatus(string? value)
    {
        var code = ExtractLeadingCode(value);
        return FailureCodes.Contains(code) ||
            code.StartsWith("F", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPendingStatus(string? value)
    {
        return string.Equals(ExtractLeadingCode(value), "N", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsSuccessText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("\u4ea4\u6613\u6210\u529f", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u4ed8\u6b3e\u6210\u529f", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u6388\u6b0a\u6210\u529f", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u8655\u7406\u6210\u529f", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsFailureText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("\u5931\u6557", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u53d6\u6d88", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u653e\u68c4", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u903e\u671f", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u984d\u5ea6\u4e0d\u8db3", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u9918\u984d\u4e0d\u8db3", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("declined", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractLeadingCode(string? value)
    {
        var cleaned = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        char[] separators =
        [
            ' ',
            '-',
            '\u2013',
            '\u2014',
            '\uff0d',
            ':',
            '\uff1a',
            ',',
            '\uff0c',
            ';',
            '\uff1b'
        ];

        var separatorIndex = cleaned.IndexOfAny(separators);
        if (separatorIndex > 0)
        {
            cleaned = cleaned[..separatorIndex];
        }

        return cleaned.Trim();
    }
}
