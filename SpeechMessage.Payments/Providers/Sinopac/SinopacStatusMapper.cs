// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Sinopac/SinopacStatusMapper.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class SinopacStatusMapper
// 主要成員：MapCreate、Map、IsProviderRejected、IsSuccessStatus、IsFailureStatus、IsPendingStatus、ContainsSuccessText、ContainsFailureText、ExtractLeadingCode
// 引用命名空間：SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
