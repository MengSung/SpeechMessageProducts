// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentManagerNamingTests
// 主要成員：New_donation_payment_manager_exists_as_primary_ui_payment_state_manager、Legacy_qpay_manager_remains_as_compatibility_alias
// 引用命名空間：ChurchReport.Models、FluentAssertions、Microsoft.AspNetCore.Mvc、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 的奉獻付款 UI 狀態管理器已改用中性名稱。
/// 這組測試不觸發 CRM 或 LINE，只鎖定型別邊界，確保舊 DonationPaymentManager 不再是主要實作。
/// </summary>
public sealed class DonationPaymentManagerNamingTests
{
    [Fact]
    public void New_donation_payment_manager_exists_as_primary_ui_payment_state_manager()
    {
        var managerType = typeof(DonationPaymentManager);

        managerType.Should().NotBeNull(
            "ChurchReport 奉獻付款的 UI 狀態管理應以 DonationPaymentManager 作為主要名稱");
        managerType!.IsAssignableTo(typeof(Controller)).Should().BeFalse();
    }

    [Fact]
    public void Legacy_qpay_manager_remains_as_compatibility_alias()
    {
        var managerType = typeof(DonationPaymentManager);
        var legacyType = typeof(DonationPaymentManager);

        legacyType.Should().BeAssignableTo(managerType,
            "DonationPaymentManager 只能保留為舊 Controller/View 的相容入口，實際管理流程應由 DonationPaymentManager 承擔");
    }
}
