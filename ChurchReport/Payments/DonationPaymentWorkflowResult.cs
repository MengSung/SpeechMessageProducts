using System.Collections.Generic;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 付款回傳流程使用的產品層結果 DTO。
/// 這個型別只描述 ChurchReport 後續流程需要的付款資料，例如 CRM 單據更新、
/// LINE 通知與結果頁顯示；它不代表永豐、高鉅、台新或任何特定金流供應商。
/// </summary>
public record DonationPaymentWorkflowResult
{
    /// <summary>金流店號或付款設定代號，通常由 provider callback 或 appsettings profile 解析而來。</summary>
    public string ShopNo { get; init; } = string.Empty;

    /// <summary>金流端回傳的付款識別碼；在永豐舊流程中通常等同 PayToken。</summary>
    public string PayToken { get; init; } = string.Empty;

    /// <summary>ChurchReport 產品端訂單編號，例如奉獻或費用單據對應的訂單號。</summary>
    public string OrderNo { get; init; } = string.Empty;

    /// <summary>金流端交易序號；只有 provider 有回傳時才會有值。</summary>
    public string ProviderTransactionId { get; init; } = string.Empty;

    /// <summary>以主要幣別表示的付款金額，例如新台幣 1200 元。</summary>
    public decimal? Amount { get; init; }

    /// <summary>以最小幣別單位表示的付款金額，例如 1200 元可表示為 120000 分。</summary>
    public string AmountMinorUnits { get; init; } = string.Empty;

    /// <summary>ChurchReport 產品實體 Id，例如費用單、奉獻預約單或其他產品單據 Id。</summary>
    public string ProductEntityId { get; init; } = string.Empty;

    /// <summary>付款所屬組織，用來讓 ChurchReport 後續流程判斷 CRM 與 LINE 通知上下文。</summary>
    public string PaymentOrganization { get; init; } = string.Empty;

    /// <summary>產品端付款分類，例如 fee、dedication_booking 或 recurring_dedication。</summary>
    public string PaymentCategory { get; init; } = string.Empty;

    /// <summary>付款方式代碼。這是產品流程的中性欄位，不應假設只屬於某一家 provider。</summary>
    public string PayType { get; init; } = string.Empty;

    /// <summary>產品流程使用的付款狀態代碼；目前沿用舊流程的 S/F 表示成功或失敗。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>給結果頁、CRM 備註或除錯紀錄使用的狀態描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>信用卡前段卡號；只有信用卡流程且 provider 有回傳時才會有值。</summary>
    public string LeftCCNo { get; init; } = string.Empty;

    /// <summary>信用卡後段卡號；只有信用卡流程且 provider 有回傳時才會有值。</summary>
    public string RightCCNo { get; init; } = string.Empty;

    /// <summary>信用卡有效年月；只有定期定額或 token 化信用卡流程需要此欄位。</summary>
    public string CCExpDate { get; init; } = string.Empty;

    /// <summary>信用卡 token；ChurchReport 用它判斷是否要保存或更新定期定額授權資訊。</summary>
    public string CCToken { get; init; } = string.Empty;

    /// <summary>
    /// provider 解析後保留下來的附加資料。
    /// 這是跨層邊界的最後保留區，主要用於相容舊欄位；新邏輯應優先使用上方具名屬性。
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
}
