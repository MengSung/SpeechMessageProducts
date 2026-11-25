/*
 * 檔案整體說明
 *
 * 目的與定位：描述取消授權及立即請款等中立訂單維護操作。公開請求只攜帶 profile 名稱與訂單識別，
 * Gateway 以伺服器端 resolver 選定 provider，避免產品層接觸銀行命令與 profile 憑證。
 *
 * 主要功能與流程：
 * IPaymentOrderMaintenance 是核心內部 capability；CancelAuthorizationAsync 與 CaptureNowAsync
 * 分別由 provider 映射到其協定命令，再回傳標準 PaymentStatusResult。
 *
 * 使用邊界與維護：
 * profile、訂單與憑證只屬單次呼叫；本檔不保存 Session、使用者或跨請求可變狀態。
 * provider 必須將呼叫端取消權杖傳遞至 I/O，借用 DI 管理的 HTTP client，並由單次操作釋放 request／response。
 * 離線測試以合成訂單與記憶體 handler 驗證，不呼叫任何金流 endpoint。
 */
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 訂單維護的 provider-neutral 請求；OrderNo 必須是伺服器端建立並持久化的穩定業務識別。
/// </summary>
public sealed record OrderMaintenanceRequest
{
    /// <summary>由伺服器端 resolver 查找的商店 profile 名稱。</summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>選用的 provider 防呆提示；Gateway 會核對其與 resolver 結果一致。</summary>
    public PaymentProviderKind? ProviderHint { get; init; }

    /// <summary>要維護的產品訂單識別，不可用顯示名稱或客戶輸入代替。</summary>
    public string OrderNo { get; init; } = string.Empty;
}

/// <summary>
/// 取消授權與立即請款的內部 provider capability；不支援的 provider 不實作此介面，
/// Gateway 必須回 UnsupportedOperation 且不得發出 I/O。
/// </summary>
internal interface IPaymentOrderMaintenance
{
    /// <summary>要求 provider 取消指定訂單授權，只有銀行明確成功回應才回傳 Cancelled。</summary>
    /// <param name="profile">Gateway 已解析的商店 profile，只供本次操作使用。</param>
    /// <param name="request">含穩定 OrderNo 的中立維護輸入。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>標準付款狀態；銀行拒絕或回應不匹配時不宣稱操作成功。</returns>
    Task<PaymentStatusResult> CancelAuthorizationAsync(
        PaymentMerchantProfile profile,
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken);

    /// <summary>要求 provider 對指定訂單立即請款，只有銀行明確成功回應才回傳 Succeeded。</summary>
    /// <param name="profile">Gateway 已解析的商店 profile，只供本次操作使用。</param>
    /// <param name="request">含穩定 OrderNo 的中立維護輸入。</param>
    /// <param name="cancellationToken">由呼叫端提供並傳遞至 provider I/O 的取消權杖。</param>
    /// <returns>標準付款狀態；傳輸結果不確定時不得回報成功。</returns>
    Task<PaymentStatusResult> CaptureNowAsync(
        PaymentMerchantProfile profile,
        OrderMaintenanceRequest request,
        CancellationToken cancellationToken);
}
