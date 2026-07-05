// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class ChurchReportPaymentContextBuilderTests
// 主要成員：BuildFromResolvedValues_contains_all_workflow_context_items、BuildFromResolvedValues_uses_unknown_payer_name_when_full_name_is_blank、CreateToolUtility
// 引用命名空間：ChurchReport.Payments、ChurchReport.Services、FluentAssertions、Microsoft.Extensions.Logging.Abstractions、Microsoft.Xrm.Sdk、SpeechMessage.Payments.Models、SpeechMessage.Payments.Workflows、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Payments;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Payments.Models;
using SpeechMessage.Payments.Workflows;
using ToolUtilityNameSpace;
using Xunit;
using static ChurchReport.Services.PaymentFeeTypeHelper;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class ChurchReportPaymentContextBuilderTests
{
    [Fact]
    public void BuildFromResolvedValues_contains_all_workflow_context_items()
    {
        var toolUtility = CreateToolUtility();
        var feeEntity = new Entity("new_fee") { Id = Guid.NewGuid() };
        var contactEntity = new Entity("contact") { Id = Guid.NewGuid() };
        var payment = new PaymentWorkflowResult
        {
            Status = PaymentStatus.Succeeded,
            ProductOrderId = "ORDER-CTX-001",
            ProviderTransactionId = "TX-CTX-001",
            Amount = 800m,
            Currency = "TWD"
        };
        var builder = new ChurchReportPaymentContextBuilder(
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance));

        var context = builder.BuildFromResolvedValues(
            toolUtility,
            feeEntity,
            payment,
            isSuccess: true,
            fullName: "王小明",
            feeType: FeeType.Dedication,
            contactEntity: contactEntity);

        context.Payment.Should().BeSameAs(payment);
        context.GetRequiredItem<ToolUtilityClass>(ChurchReportPaymentWorkflowContextKeys.ToolUtility).Should().BeSameAs(toolUtility);
        context.GetRequiredItem<Entity>(ChurchReportPaymentWorkflowContextKeys.FeeEntity).Should().BeSameAs(feeEntity);
        context.GetRequiredItem<bool>(ChurchReportPaymentWorkflowContextKeys.IsSuccess).Should().BeTrue();
        context.GetRequiredItem<string>(ChurchReportPaymentWorkflowContextKeys.FullName).Should().Be("王小明");
        context.GetRequiredItem<FeeType>(ChurchReportPaymentWorkflowContextKeys.FeeType).Should().Be(FeeType.Dedication);
        context.GetRequiredItem<Entity>(ChurchReportPaymentWorkflowContextKeys.ContactEntity).Should().BeSameAs(contactEntity);
    }

    [Fact]
    public void BuildFromResolvedValues_uses_unknown_payer_name_when_full_name_is_blank()
    {
        var builder = new ChurchReportPaymentContextBuilder(
            new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance));
        var context = builder.BuildFromResolvedValues(
            CreateToolUtility(),
            new Entity("new_fee") { Id = Guid.NewGuid() },
            new PaymentWorkflowResult
            {
                Status = PaymentStatus.Failed,
                ProductOrderId = "ORDER-CTX-002",
                Amount = 300m,
                Currency = "TWD"
            },
            isSuccess: false,
            fullName: " ",
            feeType: FeeType.Other,
            contactEntity: null);

        context.GetRequiredItem<bool>(ChurchReportPaymentWorkflowContextKeys.IsSuccess).Should().BeFalse();
        context.GetRequiredItem<string>(ChurchReportPaymentWorkflowContextKeys.FullName).Should().Be("未知付款者");
        context.GetOptionalItem<Entity>(ChurchReportPaymentWorkflowContextKeys.ContactEntity).Should().BeNull();
    }

    private static ToolUtilityClass CreateToolUtility()
    {
        var validFlag = false;
        return new ToolUtilityClass(ref validFlag);
    }
}
