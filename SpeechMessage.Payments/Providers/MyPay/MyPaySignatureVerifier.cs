// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class MyPaySignatureVerifier
// 主要成員：Validate
// 引用命名空間：SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

/// <summary>
/// MyPay callback 的最低限度欄位驗證。
/// 現有舊流程未提供可驗證的 shared secret 簽章，因此這裡先確認 callback 是否具備
/// MyPay 必要欄位與已知狀態碼，避免不完整資料進入產品 workflow。
/// </summary>
internal static class MyPaySignatureVerifier
{
    private static readonly HashSet<string> KnownPrcCodes = new(StringComparer.Ordinal)
    {
        "250",
        "290",
        "600",
        "300",
        "400",
        "260",
        "270",
        "280"
    };

    public static PaymentError Validate(IReadOnlyDictionary<string, string> fields)
    {
        var errors = new List<string>();

        // uid/key/prc/order_id 是 MyPay callback 用來識別交易與狀態的核心欄位。
        if (!fields.TryGetValue("uid", out var uid) || string.IsNullOrWhiteSpace(uid) || uid.Length != 32)
        {
            errors.Add("uid is required and must be 32 characters.");
        }

        if (!fields.TryGetValue("key", out var key) || string.IsNullOrWhiteSpace(key) || key.Length != 32)
        {
            errors.Add("key is required and must be 32 characters.");
        }

        if (!fields.TryGetValue("prc", out var prc) || !KnownPrcCodes.Contains(prc))
        {
            errors.Add("prc is missing or unsupported.");
        }

        if (!fields.TryGetValue("order_id", out var orderId) || string.IsNullOrWhiteSpace(orderId))
        {
            errors.Add("order_id is required.");
        }

        return errors.Count == 0
            ? PaymentError.None
            : new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = string.Join(" ", errors)
            };
    }
}
