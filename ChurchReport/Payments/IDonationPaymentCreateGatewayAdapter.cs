using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 產品層建立付款訂單時使用的中性 adapter 契約。
/// 此介面保留在 ChurchReport 專案中，因為 <see cref="CreOrder"/> 是舊奉獻網頁流程仍需使用的相容 DTO；
/// 可重用的 provider protocol 仍由 <c>SpeechMessage.Payments</c> 負責，避免 ChurchReport 的舊 DTO 污染核心金流專案。
/// </summary>
public interface IDonationPaymentCreateGatewayAdapter
{
    /// <summary>
    /// 將 ChurchReport 的奉獻付款輸入轉成 provider-neutral request，並交給目前設定的金流 provider 建立付款。
    /// </summary>
    Task<PaymentCreateResult> CreateCardPaymentAsync(
        QPayCreatePaymentInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 建立付款後投影成既有 ChurchReport 頁面仍使用的 <see cref="CreOrder"/> 結果。
    /// 新產品若不需要舊 DTO，應直接使用 <see cref="CreateCardPaymentAsync"/> 的 provider-neutral 結果。
    /// </summary>
    Task<CreOrder> CreateLegacyOrderAsync(
        QPayCreatePaymentInput input,
        CancellationToken cancellationToken = default);
}
