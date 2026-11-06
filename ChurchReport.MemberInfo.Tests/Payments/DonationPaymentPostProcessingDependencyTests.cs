/*
 * 檔案整體說明
 *
 * 目的與定位：以建構式契約確認產品付款後處理器依賴共用workflow與產品presenter，防止provider與產品責任重新混合。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentPostProcessingDependencyTests。
 * 測試案例入口包含 Donation_fee_payment_processor_accepts_common_workflow_and_presenter。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using ChurchReport.Payments;
using ChurchReport.Tools;
using FluentAssertions;
using SpeechMessage.Payments.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentPostProcessingDependencyTests
{
    [Fact]
    public void Donation_fee_payment_processor_accepts_common_workflow_and_presenter()
    {
        var constructorParameters = typeof(DonationFeePaymentProcessor)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().Contain(typeof(PaymentPostPaymentWorkflow));
        constructorParameters.Should().Contain(typeof(ChurchReportPaymentContextBuilder));
        constructorParameters.Should().Contain(typeof(DonationPaymentReturnPresenter));
    }
}
