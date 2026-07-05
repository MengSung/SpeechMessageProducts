// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentProductServiceNamingTests
// 主要成員：Product_payment_services_use_provider_neutral_names、Legacy_mypay_product_service_names_are_removed
// 引用命名空間：FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 的付款後產品流程服務不再以單一 provider 命名。
/// CRM 更新、LINE 通知、付款訊息組裝都屬於 ChurchReport 產品流程，
/// 服務名稱應表達「產品付款後處理」而不是誤導成 MyPay 金流協定實作。
/// </summary>
public sealed class PaymentProductServiceNamingTests
{
    [Fact]
    public void Product_payment_services_use_provider_neutral_names()
    {
        var expectedServiceNames = new[]
        {
            "ChurchReport.Services.PaymentCrmService, ChurchReport",
            "ChurchReport.Services.PaymentFeeTypeHelper, ChurchReport",
            "ChurchReport.Services.PaymentCallbackLogger, ChurchReport",
            "ChurchReport.Services.PaymentMessageBuilder, ChurchReport",
            "ChurchReport.Services.PaymentNotificationService, ChurchReport"
        };

        foreach (var expectedServiceName in expectedServiceNames)
        {
            Type.GetType(expectedServiceName).Should().NotBeNull(
                "ChurchReport 付款後流程服務應以 provider-neutral 名稱呈現，避免把 CRM/LINE/收費單流程誤認為 MyPay 協定實作");
        }
    }

    [Fact]
    public void Legacy_mypay_product_service_names_are_removed()
    {
        var legacyServiceNames = new[]
        {
            "ChurchReport.Services.MyPayCrmService, ChurchReport",
            "ChurchReport.Services.MyPayFeeTypeHelper, ChurchReport",
            "ChurchReport.Services.MyPayLogger, ChurchReport",
            "ChurchReport.Services.MyPayMessageBuilder, ChurchReport",
            "ChurchReport.Services.MyPayNotificationService, ChurchReport"
        };

        foreach (var legacyServiceName in legacyServiceNames)
        {
            Type.GetType(legacyServiceName).Should().BeNull(
                "這些服務已經只消費 provider-neutral payment result，不應保留 MyPay 開頭的產品流程型別名稱");
        }
    }
}
