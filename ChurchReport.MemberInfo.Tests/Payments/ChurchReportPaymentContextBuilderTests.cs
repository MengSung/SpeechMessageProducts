/*
 * 檔案整體說明
 *
 * 目的與定位：驗證已解析付款資料完整進入workflow context，缺付款人姓名時保留安全顯示後備。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchReportPaymentContextBuilderTests。
 * 測試案例入口包含 BuildFromResolvedValues_contains_all_workflow_context_items、
 * BuildFromResolvedValues_uses_unknown_payer_name_when_full_name_is_blank、CreateToolUtility。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
