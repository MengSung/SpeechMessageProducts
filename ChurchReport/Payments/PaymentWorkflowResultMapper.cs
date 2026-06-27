using System.Collections.Generic;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// 將金流核心的 callback result 轉成 ChurchReport 產品流程使用的付款結果。
/// 這層只把 provider-neutral 欄位整理給 CRM、LINE 與既有 workflow，
/// 不重新解析 provider callback，也不依 provider 原始狀態碼做商業判斷。
/// </summary>
public sealed class PaymentWorkflowResultMapper
{
    /// <summary>
    /// 建立 ChurchReport 專用 workflow result，讓後續服務可以更新費用、送 LINE 或顯示結果頁。
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
        // provider_message 是核心各 provider 統一塞入的較佳訊息鍵；
        // message 是相容既有流程或部分 provider sample 的退路。
        if (providerData.TryGetValue("provider_message", out var providerMessage))
        {
            return providerMessage;
        }

        return providerData.TryGetValue("message", out var message) ? message : string.Empty;
    }
}

/// <summary>
/// ChurchReport 產品層的付款結果模型。
/// 這個模型允許 CRM/LINE workflow 使用 provider-neutral 狀態與 sanitized provider data，
/// 但不把永豐、高鉅、台新的原始 DTO 暴露給產品流程。
/// </summary>
public sealed record PaymentWorkflowResult
{
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderTransactionId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string ProviderMessage { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
}
