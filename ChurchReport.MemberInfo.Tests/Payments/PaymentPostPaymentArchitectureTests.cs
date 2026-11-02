// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentArchitectureTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentPostPaymentArchitectureTests
// 主要成員：Tspg_controller_depends_on_common_post_payment_workflow、Donation_fee_payment_processor_depends_on_common_post_payment_workflow、Donation_fee_payment_processor_keeps_mvc_presentation_in_churchreport、ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project
// 引用命名空間：System.Reflection、ChurchReport.Controllers、ChurchReport.Payments、ChurchReport.Tools、FluentAssertions、SpeechMessage.Payments.Workflows、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
