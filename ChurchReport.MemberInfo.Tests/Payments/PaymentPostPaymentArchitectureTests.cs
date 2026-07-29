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

public sealed class PaymentPostPaymentArchitectureTests
{
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

    [Fact]
    public void ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project()
    {
        typeof(ChurchReportPaymentRecordUpdater).Assembly.GetName().Name.Should().Be("SpeechMessageProducts.ChurchReport");
        typeof(ChurchReportPaymentPayerNotifier).Assembly.GetName().Name.Should().Be("SpeechMessageProducts.ChurchReport");
    }
}
