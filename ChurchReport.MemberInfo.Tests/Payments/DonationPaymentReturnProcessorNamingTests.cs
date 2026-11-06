/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 ChurchReport 付款完成後的產品流程已改用中性命名。 這些流程負責 CRM 更新、LINE 通知與畫面回應，屬於 ChurchReport 產品層， 不應再用 QPay 這種永豐金流 
 * provider 名稱作為相容入口。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentReturnProcessorNamingTests。
 * - 保護收費單付款完成的中性 MVC 入口。 在實際產品組件查詢指定型別，偵測入口遭刪除或搬移；斷言存在且繼承 Controller，不建立付款或使用者狀態。
 * - 保護定期奉獻回傳的中性 MVC 入口。 查詢實際產品組件以偵測型別遺失，斷言入口存在且可作為 Controller；不觸發實際扣款或對外通知。
 * - 保護付款結果與除錯工具保持中性公開入口。 對實際產品組件查詢兩個完整名稱，以刪除任一工具為故障情境；斷言兩者存在，不執行日誌寫入。
 * - 保護既定移除四個舊付款包裝型別的契約。 在真實產品組件逐項查詢可能被重新加入的完整名稱；斷言命中集合為空，避免舊入口與共用流程並存。
 * 測試案例入口包含 New_donation_fee_payment_processor_exists_as_primary_fee_return_processor、
 * New_recurring_donation_payment_processor_exists_as_primary_recurring_return_processor、
 * New_payment_result_helper_and_debug_logger_exist、
 * ChurchReport_assembly_does_not_expose_legacy_qpay_payment_workflow_wrappers。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using ChurchReport.Tools;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 付款完成後的產品流程已改用中性命名。
/// 這些流程負責 CRM 更新、LINE 通知與畫面回應，屬於 ChurchReport 產品層，
/// 不應再用 QPay 這種永豐金流 provider 名稱作為相容入口。
/// </summary>
public sealed class DonationPaymentReturnProcessorNamingTests
{
    /// <summary>
    /// 保護收費單付款完成的中性 MVC 入口。
    /// 在實際產品組件查詢指定型別，偵測入口遭刪除或搬移；斷言存在且繼承 Controller，不建立付款或使用者狀態。
    /// </summary>
    [Fact]
    public void New_donation_fee_payment_processor_exists_as_primary_fee_return_processor()
    {
        var processorType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Tools.DonationFeePaymentProcessor");

        processorType.Should().NotBeNull("收費單付款完成流程應由 DonationFeePaymentProcessor 作為主要入口");
        processorType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    /// <summary>
    /// 保護定期奉獻回傳的中性 MVC 入口。
    /// 查詢實際產品組件以偵測型別遺失，斷言入口存在且可作為 Controller；不觸發實際扣款或對外通知。
    /// </summary>
    [Fact]
    public void New_recurring_donation_payment_processor_exists_as_primary_recurring_return_processor()
    {
        var processorType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Tools.RecurringDonationPaymentProcessor");

        processorType.Should().NotBeNull("定期定額付款完成流程應由 RecurringDonationPaymentProcessor 作為主要入口");
        processorType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    /// <summary>
    /// 保護付款結果與除錯工具保持中性公開入口。
    /// 對實際產品組件查詢兩個完整名稱，以刪除任一工具為故障情境；斷言兩者存在，不執行日誌寫入。
    /// </summary>
    [Fact]
    public void New_payment_result_helper_and_debug_logger_exist()
    {
        typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Tools.DonationPaymentResultHelper")
            .Should().NotBeNull("付款結果判斷 helper 應使用中性命名");
        typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Tools.DonationPaymentDebugLogger")
            .Should().NotBeNull("付款除錯記錄 helper 應使用中性命名");
    }

    /// <summary>
    /// 保護既定移除四個舊付款包裝型別的契約。
    /// 在真實產品組件逐項查詢可能被重新加入的完整名稱；斷言命中集合為空，避免舊入口與共用流程並存。
    /// </summary>
    [Fact]
    public void ChurchReport_assembly_does_not_expose_legacy_qpay_payment_workflow_wrappers()
    {
        var removedCompatibilityTypeNames = new[]
        {
            "ChurchReport.Tools.QPayDedicationBookingProcessor",
            "ChurchReport.Tools.QPayFeeProcessor",
            "ChurchReport.Tools.QPayPaymentDebugLogger",
            "ChurchReport.Tools.QPayPaymentResultHelper"
        };

        var assembly = typeof(DonationFeePaymentProcessor).Assembly;
        var existingCompatibilityTypes = removedCompatibilityTypeNames
            .Where(typeName => assembly.GetType(typeName, throwOnError: false) != null)
            .OrderBy(typeName => typeName)
            .ToArray();

        existingCompatibilityTypes.Should().BeEmpty(
            "金流流程已改用中性命名，ChurchReport 產品層不應再保留 QPay 舊名稱相容包裝");
    }
}
