/*
 * 檔案整體說明
 *
 * 目的與定位：確認 ChurchReport 的奉獻付款 processor 已經使用 DonationPayment 命名。 DonationPaymentProcessor 是 ChurchReport 
 * 產品層的付款後流程處理器， 會處理 CRM fee、奉獻收據、LINE 通知與頁面回傳。這些都不是永豐 QPay 協定本身。 因此 processor 不能再提供 QPayProcessor alias，否則未來高鉅、
 * 台新或其他產品共用流程時， 會看起來像是所有付款都必須經過永豐 QPay。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentProcessorNamingTests。
 * - 保護奉獻付款後流程的中性處理器入口。 以完整名稱查詢實際產品組件，讓刪除或錯誤搬移型別確實造成失敗；斷言處理器存在，不呼叫銀行、CRM 或 LINE。
 * - 保護舊供應商處理器相容包裝已移除的架構契約。 在已載入的產品組件查詢舊型別，模擬未來誤加回舊入口；斷言不存在，不能因組件查詢失敗而假綠燈。
 * - 保護產品處理器必須注入中性付款建立轉接器。 反射實際公開建構式，偵測移除依賴或把依賴改為可選參數的退化；斷言轉接器參數存在且沒有預設值。
 * 測試案例入口包含 New_donation_payment_processor_exists_as_primary_product_workflow_processor、
 * Legacy_qpay_processor_alias_should_not_remain、
 * Donation_payment_processor_constructors_require_neutral_gateway_create_adapter。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Reflection;
using ChurchReport.Payments;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 確認 ChurchReport 的奉獻付款 processor 已經使用 DonationPayment 命名。
///
/// DonationPaymentProcessor 是 ChurchReport 產品層的付款後流程處理器，
/// 會處理 CRM fee、奉獻收據、LINE 通知與頁面回傳。這些都不是永豐 QPay 協定本身。
/// 因此 processor 不能再提供 QPayProcessor alias，否則未來高鉅、台新或其他產品共用流程時，
/// 會看起來像是所有付款都必須經過永豐 QPay。
/// </summary>
public sealed class DonationPaymentProcessorNamingTests
{
    /// <summary>
    /// 保護奉獻付款後流程的中性處理器入口。
    /// 以完整名稱查詢實際產品組件，讓刪除或錯誤搬移型別確實造成失敗；斷言處理器存在，不呼叫銀行、CRM 或 LINE。
    /// </summary>
    [Fact]
    public void New_donation_payment_processor_exists_as_primary_product_workflow_processor()
    {
        var processorType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor");

        processorType.Should().NotBeNull(
            "ChurchReport 產品層付款後流程應由 DonationPaymentProcessor 作為主要類別名稱");
    }

    /// <summary>
    /// 保護舊供應商處理器相容包裝已移除的架構契約。
    /// 在已載入的產品組件查詢舊型別，模擬未來誤加回舊入口；斷言不存在，不能因組件查詢失敗而假綠燈。
    /// </summary>
    [Fact]
    public void Legacy_qpay_processor_alias_should_not_remain()
    {
        typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.WebServiceConnector.QPayProcessor").Should().BeNull(
            "QPayProcessor 是產品層舊 alias；重構後應直接使用 DonationPaymentProcessor，" +
            "舊外部 URL 可由 route 保留，但 C# 類別名稱不應再保留 QPay alias");
    }

    /// <summary>
    /// 保護產品處理器必須注入中性付款建立轉接器。
    /// 反射實際公開建構式，偵測移除依賴或把依賴改為可選參數的退化；斷言轉接器參數存在且沒有預設值。
    /// </summary>
    [Fact]
    public void Donation_payment_processor_constructors_require_neutral_gateway_create_adapter()
    {
        var processorType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor");

        processorType.Should().NotBeNull();
        var adapterParameters = processorType!
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(DonationPaymentCreateGatewayAdapter))
            .ToArray();

        adapterParameters.Should().NotBeEmpty(
            "新的主要 processor 應要求 DonationPaymentCreateGatewayAdapter，避免重新引入 QPay 命名的 create adapter");
        adapterParameters.Should().OnlyContain(parameter => !parameter.HasDefaultValue);
    }
}
