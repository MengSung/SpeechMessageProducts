// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorNamingTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentProcessorNamingTests
// 主要成員：New_donation_payment_processor_exists_as_primary_product_workflow_processor、Legacy_qpay_processor_alias_should_not_remain、Donation_payment_processor_constructors_require_neutral_gateway_create_adapter
// 引用命名空間：System.Reflection、ChurchReport.Payments、FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    private const string ChurchReportAssemblyName = "SpeechMessageProducts.ChurchReport";

    [Fact]
    public void New_donation_payment_processor_exists_as_primary_product_workflow_processor()
    {
        var processorType = Type.GetType($"ChurchReport.WebServiceConnector.DonationPaymentProcessor, {ChurchReportAssemblyName}");

        processorType.Should().NotBeNull(
            "ChurchReport 產品層付款後流程應由 DonationPaymentProcessor 作為主要類別名稱");
    }

    [Fact]
    public void Legacy_qpay_processor_alias_should_not_remain()
    {
        Type.GetType($"ChurchReport.WebServiceConnector.QPayProcessor, {ChurchReportAssemblyName}").Should().BeNull(
            "QPayProcessor 是產品層舊 alias；重構後應直接使用 DonationPaymentProcessor，" +
            "舊外部 URL 可由 route 保留，但 C# 類別名稱不應再保留 QPay alias");
    }

    [Fact]
    public void Donation_payment_processor_constructors_require_neutral_gateway_create_adapter()
    {
        var processorType = Type.GetType($"ChurchReport.WebServiceConnector.DonationPaymentProcessor, {ChurchReportAssemblyName}");

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
