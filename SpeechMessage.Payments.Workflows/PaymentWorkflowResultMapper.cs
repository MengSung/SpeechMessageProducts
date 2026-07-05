// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments.Workflows/PaymentWorkflowResultMapper.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class PaymentWorkflowResultMapper、record PaymentWorkflowResult
// 主要成員：Map、ReadProviderMessage、Status、ProductOrderId、ProviderTransactionId、Amount、Currency、ProviderMessage
// 引用命名空間：SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// 將金流核心解析完成的 callback 結果投影成產品流程容易使用的摘要模型。
/// 此類別只整理中立欄位；付款成功後要更新維修單、會員期限、發票收款、
/// 奉獻紀錄或發送通知，仍由各產品自行實作。
/// </summary>
public sealed class PaymentWorkflowResultMapper
{
    /// <summary>
    /// 從 <see cref="PaymentCallbackResult"/> 取出產品流程常用欄位。
    /// Provider 原始資料保留在 <see cref="PaymentWorkflowResult.ProviderData"/>，
    /// 供產品端在必要時做對帳、紀錄或通知訊息。
    /// </summary>
    public PaymentWorkflowResult Map(PaymentCallbackResult result)
    {
        return new PaymentWorkflowResult
        {
            Status = result.Status,
            ProductOrderId = result.ProductOrderId,
            ProviderTransactionId = result.ProviderTransactionId,
            Amount = result.Amount,
            Currency = result.Currency,
            ProviderMessage = ReadProviderMessage(result.ProviderData),
            ProviderData = result.ProviderData
        };
    }

    private static string ReadProviderMessage(IReadOnlyDictionary<string, string> providerData)
    {
        // 多數 provider parser 會把主要訊息正規化成 provider_message；
        // message 是相容部分舊 payload 或測試資料的 fallback。
        if (providerData.TryGetValue("provider_message", out var providerMessage))
        {
            return providerMessage;
        }

        return providerData.TryGetValue("message", out var message) ? message : string.Empty;
    }
}

/// <summary>
/// 產品流程友善的付款 callback 結果。
/// 共用層只負責狀態、訂單號、金額、交易流水號與 provider 訊息的正規化；
/// 各產品仍自行決定成功、失敗、待處理時要更新哪些資料。
/// </summary>
public sealed record PaymentWorkflowResult
{
    /// <summary>金流核心正規化後的付款狀態。</summary>
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    /// <summary>產品端原始訂單號。</summary>
    public string ProductOrderId { get; init; } = string.Empty;
    /// <summary>金流供應商交易流水號或授權交易識別碼。</summary>
    public string ProviderTransactionId { get; init; } = string.Empty;
    /// <summary>callback 中確認到的付款金額；部分 provider 或失敗狀態可能為空。</summary>
    public decimal? Amount { get; init; }
    /// <summary>付款幣別，預設為 TWD。</summary>
    public string Currency { get; init; } = "TWD";
    /// <summary>供產品端紀錄或通知使用的 provider 訊息。</summary>
    public string ProviderMessage { get; init; } = string.Empty;
    /// <summary>經過金流核心清理後的 provider 原始延伸欄位。</summary>
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
}
