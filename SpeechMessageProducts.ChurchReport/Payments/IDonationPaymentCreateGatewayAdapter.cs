/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 產品層建立付款訂單時使用的中性 adapter 契約。 此介面保留在 ChurchReport 專案中，因為 CreOrder 是舊奉獻網頁流程仍需使用的相容 DTO； 可重用的 
 * provider protocol 仍由 SpeechMessage.Payments 負責，避免 ChurchReport 的舊 DTO 污染核心金流專案。
 *
 * 主要功能與流程：
 * 本檔宣告 IDonationPaymentCreateGatewayAdapter。
 * - 將 ChurchReport 的奉獻付款輸入轉成 provider-neutral request，並交給目前設定的金流 provider 建立付款。
 * - 建立付款後投影成既有 ChurchReport 頁面仍使用的 CreOrder 結果。 新產品若不需要舊 DTO，應直接使用 CreateCardPaymentAsync 的 provider-neutral 結果。
 * 資料／選項欄位包含 IDonationPaymentCreateGatewayAdapter，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs、
 * SpeechMessageProducts.ChurchReport/Models/ContextDictionary.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs 追查目前工作樹中的合作／使用位置。
 */
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
        DonationPaymentCreateInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 建立付款後投影成既有 ChurchReport 頁面仍使用的 <see cref="CreOrder"/> 結果。
    /// 新產品若不需要舊 DTO，應直接使用 <see cref="CreateCardPaymentAsync"/> 的 provider-neutral 結果。
    /// </summary>
    Task<CreOrder> CreateLegacyOrderAsync(
        DonationPaymentCreateInput input,
        CancellationToken cancellationToken = default);
}
