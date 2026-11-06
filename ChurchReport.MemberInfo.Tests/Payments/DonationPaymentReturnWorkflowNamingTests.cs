/*
 * 檔案整體說明
 *
 * 目的與定位：驗證付款後產品流程使用中性結果 DTO，且舊供應商結果型別已移除。 以實際產品組件的完整型別名稱偵測 DTO 遺失或相容別名回流，不執行付款回呼。 只讀反射資訊，不建立 DTO 實例或保存任何使用者、
 * 付款及外部連線狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentReturnWorkflowNamingTests。
 * - 保護舊供應商付款結果 DTO 已移除。 在已載入的真實產品組件查詢舊完整名稱，讓重新引入相容 DTO 的退化可被偵測；斷言舊型別不存在。
 * - 保護產品流程使用中性付款結果 DTO。 以完整型別名稱查詢真實產品組件，偵測結果型別遭刪除或搬到錯誤邊界；斷言新 DTO 存在。
 * 測試案例入口包含 Legacy_qpay_workflow_result_should_not_remain_after_neutral_rename、
 * New_donation_payment_workflow_result_exists_after_rename。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證付款後產品流程使用中性結果 DTO，且舊供應商結果型別已移除。
/// 以實際產品組件的完整型別名稱偵測 DTO 遺失或相容別名回流，不執行付款回呼。
/// 只讀反射資訊，不建立 DTO 實例或保存任何使用者、付款及外部連線狀態。
/// </summary>
public sealed class DonationPaymentReturnWorkflowNamingTests
{
    /// <summary>
    /// 保護舊供應商付款結果 DTO 已移除。
    /// 在已載入的真實產品組件查詢舊完整名稱，讓重新引入相容 DTO 的退化可被偵測；斷言舊型別不存在。
    /// </summary>
    [Fact]
    public void Legacy_qpay_workflow_result_should_not_remain_after_neutral_rename()
    {
        // CRM 更新與 LINE 通知流程已經改用 DonationPaymentWorkflowResult。
        // 舊 QPayWorkflowPaymentResult 不應再保留成 alias，避免未來高鉅、台新或新金流看起來也在依賴永豐名稱。
        var resultType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Payments.QPayWorkflowPaymentResult");

        resultType.Should().BeNull();
    }

    /// <summary>
    /// 保護產品流程使用中性付款結果 DTO。
    /// 以完整型別名稱查詢真實產品組件，偵測結果型別遭刪除或搬到錯誤邊界；斷言新 DTO 存在。
    /// </summary>
    [Fact]
    public void New_donation_payment_workflow_result_exists_after_rename()
    {
        // ChurchReport 產品流程應該使用 DonationPaymentWorkflowResult，而不是使用永豐專屬的 QPay 名稱。
        var resultType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Payments.DonationPaymentWorkflowResult");

        resultType.Should().NotBeNull("新的產品中性 DTO 會成為 ChurchReport 付款回傳流程的主要型別");
    }
}
