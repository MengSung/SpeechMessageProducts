/*
 * 檔案整體說明
 *
 * 目的與定位：建立付款的通用請求，以不可變 Money 保存總額與 ISO 幣別；provider 相容欄位放在 Metadata，不讓宿主產品綁定 QPay/MyPay/TSPG 模型。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCreateRequest。
 * 資料／選項欄位包含 PaymentCreateRequest、ProfileName、ProviderHint、ProductOrderId、Total（Money）、Description、
 * PaymentMethod、PaymentMethodSubType、Callbacks、Customer、Items、Metadata，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * Money 是純值，不保存使用者、租戶或連線狀態；舊 Amount／Currency 僅為 Obsolete 轉接，不換匯、不捨入。
 * 修改欄位時須核對 provider mapper、workflow、host factory 及 ChurchReport 相容呼叫端；此模型不執行授權、I/O 或外部資源管理。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Localization;

namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款的通用請求。
/// 此 DTO 不使用任何 provider 專屬欄位名稱；必要的 provider 相容參數放在 Metadata，
/// 由各 provider mapper 解讀，避免宿主產品綁死在 QPay/MyPay/TSPG 模型上。
/// </summary>
public sealed record PaymentCreateRequest
{
    private Money total = new(0m, "TWD");

    /// <summary>選用的商店 profile 名稱；空白時才由 resolver 使用已設定的預設 profile。</summary>
    public string ProfileName { get; init; } = string.Empty;
    // ProviderHint 用於防呆：指定 provider 時，gateway 會檢查 profile.Provider 是否一致。
    /// <summary>可選 provider 防呆提示；不是商店或組織的授權來源。</summary>
    public PaymentProviderKind? ProviderHint { get; init; }
    /// <summary>產品端訂單識別字串，不承載憑證或 profile 選擇權限。</summary>
    public string ProductOrderId { get; init; } = string.Empty;
    /// <summary>付款總額及其 ISO 幣別；完整保存 decimal，不換匯或依顯示精度捨入。</summary>
    public Money Total
    {
        get => total;
        init => total = value;
    }

    /// <summary>相容既有呼叫端的金額轉接；新程式應設定 <see cref="Total"/>。</summary>
    [Obsolete("Use Total instead.")]
    public decimal Amount
    {
        get => total.Amount;
        init => total = new Money(value, total.Currency);
    }

    /// <summary>相容既有呼叫端的幣別轉接；新程式應設定 <see cref="Total"/>。</summary>
    [Obsolete("Use Total instead.")]
    public string Currency
    {
        get => total.Currency;
        init => total = new Money(total.Amount, value);
    }
    /// <summary>顯示於 provider 訂單或付款頁的說明文字。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>由 provider mapper 轉換為各家協定欄位的中立付款方式。</summary>
    public string PaymentMethod { get; init; } = string.Empty;
    /// <summary>付款方式的中立子類型，例如一次付清或定期付款方案。</summary>
    public string PaymentMethodSubType { get; init; } = string.Empty;
    /// <summary>provider 回呼與付款結果網址；不作為呼叫端身分或授權依據。</summary>
    public PaymentCallbacks Callbacks { get; init; } = new();
    /// <summary>付款者的中立聯絡欄位；不包含產品 CRM 型別。</summary>
    public PaymentCustomer Customer { get; init; } = new();
    /// <summary>每一項的金額及幣別皆以 Money 表示；不代表各項可跨幣別加總。</summary>
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
    // Metadata 承載舊流程必要但非通用的欄位，例如 QPay Param1/Param2 或 MyPay PFN。
    /// <summary>供 provider adapter 解讀的產品相容中繼資料；不得放入秘密或用來替代伺服器端 profile 授權。</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
