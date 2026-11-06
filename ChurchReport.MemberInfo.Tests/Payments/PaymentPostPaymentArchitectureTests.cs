/*
 * 檔案整體說明
 *
 * 目的與定位：驗證付款控制器及處理器經由建構式依賴共用付款後工作流程，並保留產品層 presenter。 以反射檢查依賴與宣告組件，偵測 CRM／LINE 產品處理器被錯移到可重用組件的架構退化。 不建立服務或發出通知，
 * 檢查結果僅存活於當次案例。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentPostPaymentArchitectureTests。
 * - 保護銀行回傳控制器使用共用付款後流程。 檢查公開建構式，偵測流程依賴被移除的退化；斷言參數包含 PaymentPostPaymentWorkflow，無須外部銀行服務。
 * - 保護奉獻付款完成處理器使用共用工作流程。 反射公開建構式以偵測重新複製產品流程的退化；斷言存在共用 PaymentPostPaymentWorkflow 依賴。
 * - 保護付款回傳畫面組裝仍由產品層負責。 檢查處理器建構式，偵測產品 presenter 被移除或繞過；斷言依賴 DonationPaymentReturnPresenter。
 * - 保護 CRM 與 LINE 產品處理器不得移入可重用付款組件。 以實際宣告組件檢查搬移風險；斷言兩者屬於目前產品組件，且與共用流程組件不同。
 * 測試案例入口包含 Tspg_controller_depends_on_common_post_payment_workflow、
 * Donation_fee_payment_processor_depends_on_common_post_payment_workflow、
 * Donation_fee_payment_processor_keeps_mvc_presentation_in_churchreport、
 * ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Reflection;
using ChurchReport.Controllers;
using ChurchReport.Payments;
using ChurchReport.Tools;
using FluentAssertions;
using SpeechMessage.Payments.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證付款控制器及處理器經由建構式依賴共用付款後工作流程，並保留產品層 presenter。
/// 以反射檢查依賴與宣告組件，偵測 CRM／LINE 產品處理器被錯移到可重用組件的架構退化。
/// 不建立服務或發出通知，檢查結果僅存活於當次案例。
/// </summary>
public sealed class PaymentPostPaymentArchitectureTests
{
    /// <summary>
    /// 保護銀行回傳控制器使用共用付款後流程。
    /// 檢查公開建構式，偵測流程依賴被移除的退化；斷言參數包含 PaymentPostPaymentWorkflow，無須外部銀行服務。
    /// </summary>
    [Fact]
    public void Tspg_controller_depends_on_common_post_payment_workflow()
    {
        typeof(TSPGController)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(PaymentPostPaymentWorkflow));
    }

    /// <summary>
    /// 保護奉獻付款完成處理器使用共用工作流程。
    /// 反射公開建構式以偵測重新複製產品流程的退化；斷言存在共用 PaymentPostPaymentWorkflow 依賴。
    /// </summary>
    [Fact]
    public void Donation_fee_payment_processor_depends_on_common_post_payment_workflow()
    {
        typeof(DonationFeePaymentProcessor)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(PaymentPostPaymentWorkflow));
    }

    /// <summary>
    /// 保護付款回傳畫面組裝仍由產品層負責。
    /// 檢查處理器建構式，偵測產品 presenter 被移除或繞過；斷言依賴 DonationPaymentReturnPresenter。
    /// </summary>
    [Fact]
    public void Donation_fee_payment_processor_keeps_mvc_presentation_in_churchreport()
    {
        typeof(DonationFeePaymentProcessor)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(DonationPaymentReturnPresenter));
    }

    /// <summary>
    /// 保護 CRM 與 LINE 產品處理器不得移入可重用付款組件。
    /// 以實際宣告組件檢查搬移風險；斷言兩者屬於目前產品組件，且與共用流程組件不同。
    /// </summary>
    [Fact]
    public void ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project()
    {
        typeof(ChurchReportPaymentRecordUpdater).Assembly.GetName().Name.Should().Be("SpeechMessageProducts.ChurchReport");
        typeof(ChurchReportPaymentPayerNotifier).Assembly.GetName().Name.Should().Be("SpeechMessageProducts.ChurchReport");
        typeof(ChurchReportPaymentRecordUpdater).Assembly.Should().NotBeSameAs(typeof(PaymentPostPaymentWorkflow).Assembly);
        typeof(ChurchReportPaymentPayerNotifier).Assembly.Should().NotBeSameAs(typeof(PaymentPostPaymentWorkflow).Assembly);
    }
}
