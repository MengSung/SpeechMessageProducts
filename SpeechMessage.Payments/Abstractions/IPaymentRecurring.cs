/*
 * 檔案整體說明
 *
 * 目的與定位：定義定期扣款建立、查詢與停止的 provider-neutral 結果及核心內部 capability。
 * 產品層透過 PaymentGateway 操作，不直接依賴永豐 RegularQuery 或 OrderMaintain 的 wire DTO。
 *
 * 主要功能與流程：
 * 建立沿用 Money-first PaymentCreateRequest；provider 強制指定定期付款類型並驗證排程欄位。
 * 查詢結果以每筆穩定 OrderNo 保留子扣款列，任何空白或重複 ID 都整份 fail closed；
 * StopRecurringAsync 只有銀行明確確認停止後才回傳 Cancelled。
 *
 * 使用邊界與維護：
 * 請求、profile 與合成查詢候選只屬單次呼叫；結果集合必須是唯讀快照，不得引用 provider 或可變 response list。
 * provider 借用 DI 管理的 HTTP client，呼叫端取消傳遞至 I/O，request／response 由 provider 確定性 Dispose。
 * 離線測試使用合成 profile 與記憶體 handler；不連線永豐、正式金流或任何組織。
 */
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>單筆定期扣款結果；OrderNo 是該次業務扣款的穩定識別，不以日期或顯示文字去重。</summary>
public sealed record RecurringChargeResult
{
    /// <summary>provider 回報的扣款序號。</summary>
    public int Number { get; init; }

    /// <summary>該次扣款的穩定 provider 訂單識別。</summary>
    public string OrderNo { get; init; } = string.Empty;

    /// <summary>provider 原樣回報的扣款時間字串；保留來源格式供產品轉換，不猜測時區。</summary>
    public string DeductTime { get; init; } = string.Empty;

    /// <summary>provider 原樣回報的扣款結果代碼，不直接視為產品端入帳證據。</summary>
    public string DeductStatus { get; init; } = string.Empty;
}

/// <summary>
/// 定期扣款建立或查詢的中立結果。Charges 是本次呼叫完成驗證後建立的唯讀副本；
/// 查詢中任何重複或缺少的穩定 OrderNo 都必須拒絕整個集合。
/// </summary>
public sealed record RecurringResult
{
    /// <summary>定期操作的標準付款狀態；建立待使用者授權時為 Pending。</summary>
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;

    /// <summary>定期主單的產品或 provider 識別。</summary>
    public string MasterOrderNo { get; init; } = string.Empty;

    /// <summary>provider 交易參照；不得用來取代產品端穩定資料列 ID。</summary>
    public string ProviderOrderRef { get; init; } = string.Empty;

    /// <summary>建立定期扣款時由 provider 回傳的付款頁絕對網址。</summary>
    public string PaymentPageUrl { get; init; } = string.Empty;

    /// <summary>provider 回報的定期方案狀態代碼，原樣保留但不作跨租戶授權依據。</summary>
    public string RecurringStatus { get; init; } = string.Empty;

    /// <summary>經完整 identity validation 後發布的子扣款唯讀集合。</summary>
    public IReadOnlyList<RecurringChargeResult> Charges { get; init; } =
        Array.Empty<RecurringChargeResult>();

    /// <summary>固定分類且不含憑證、卡片資料或原始銀行本文的錯誤。</summary>
    public PaymentError Error { get; init; } = PaymentError.None;
}

/// <summary>查詢定期扣款主單與其子扣款的 provider-neutral 請求。</summary>
public sealed record RecurringQueryRequest
{
    /// <summary>由伺服器端 resolver 查找的商店 profile 名稱。</summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>選用的 provider 防呆提示；不是商店授權來源。</summary>
    public PaymentProviderKind? ProviderHint { get; init; }

    /// <summary>由伺服器建立並持久化的定期主單識別。</summary>
    public string MasterOrderNo { get; init; } = string.Empty;
}

/// <summary>
/// Provider 對定期扣款的內部能力。MyPay／台新不實作時 Gateway 必須 fail closed，
/// 不得建立替代網路操作。
/// </summary>
internal interface IPaymentRecurring
{
    /// <summary>以 PaymentCreateRequest 建立定期扣款；provider 會覆寫子類型為 REGULAR 並驗證排程資料。</summary>
    /// <param name="profile">Gateway 已解析的單一商店 profile，只供本次操作。</param>
    /// <param name="request">Money-first 建單資料與產品提供的定期排程中繼資料。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>不含明文綁卡 token 或 provider SDK 物件的中立結果。</returns>
    Task<RecurringResult> CreateRecurringAsync(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request,
        CancellationToken cancellationToken);

    /// <summary>查詢定期主單及每筆扣款，並在完整驗證穩定識別後發布唯讀結果。</summary>
    /// <param name="profile">Gateway 已解析的單一商店 profile，只供本次操作。</param>
    /// <param name="request">定期主單查詢識別。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>完整唯讀子扣款快照，或不含部分資料的失敗結果。</returns>
    Task<RecurringResult> QueryRecurringAsync(
        PaymentMerchantProfile profile,
        RecurringQueryRequest request,
        CancellationToken cancellationToken);

    /// <summary>停止定期扣款；銀行回應必須匹配主單與停止命令後才發布 Cancelled。</summary>
    /// <param name="profile">Gateway 已解析的單一商店 profile，只供本次操作。</param>
    /// <param name="request">含定期主單識別的中立維護輸入。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>標準付款狀態，不將 callback 或請求送出本身當成停止成功。</returns>
    Task<PaymentStatusResult> StopRecurringAsync(
        PaymentMerchantProfile profile,
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken);
}
