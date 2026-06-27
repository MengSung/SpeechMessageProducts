using System.Collections.Generic;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 既有 QPay processor 使用的 workflow DTO。
/// 它保留舊欄位名稱，讓既有 CRM/LINE/結果頁程式不用直接認識新的 PaymentStatusResult；
/// 但 provider 原始 DTO、簽章、加解密與狀態碼解析仍不應放在這裡。
/// </summary>
public sealed record QPayWorkflowPaymentResult
{
    public string ShopNo { get; init; } = string.Empty;
    public string PayToken { get; init; } = string.Empty;
    public string OrderNo { get; init; } = string.Empty;
    public string ProviderTransactionId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string AmountMinorUnits { get; init; } = string.Empty;
    public string ProductEntityId { get; init; } = string.Empty;
    public string PaymentOrganization { get; init; } = string.Empty;
    public string PaymentCategory { get; init; } = string.Empty;
    public string PayType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string LeftCCNo { get; init; } = string.Empty;
    public string RightCCNo { get; init; } = string.Empty;
    public string CCExpDate { get; init; } = string.Empty;
    public string CCToken { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
}
