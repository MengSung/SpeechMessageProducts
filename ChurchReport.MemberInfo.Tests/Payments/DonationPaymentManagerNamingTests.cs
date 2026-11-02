// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentManagerNamingTests
// 主要成員：New_donation_payment_manager_exists_as_primary_ui_payment_state_manager、Legacy_qpay_manager_does_not_remain_as_compatibility_alias
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
/// 這組測試不觸發 CRM 或 LINE，只鎖定型別邊界，確認中性 manager 存在，且舊供應商別名不再形成第二個入口。
/// </summary>
public sealed class DonationPaymentManagerNamingTests
{
    /// <summary>
    /// 保護付款 UI 的中性入口仍為 MVC Controller。
    /// 以完整型別名稱查詢真實產品組件，模擬型別被刪除或移到錯誤組件；斷言型別存在且可作為 Controller，不建立任何 Session 或外部連線。
    /// </summary>
    [Fact]
    public void New_donation_payment_manager_exists_as_primary_ui_payment_state_manager()
    {
        var managerType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Models.DonationPaymentManager");

        managerType.Should().NotBeNull(
            "ChurchReport 奉獻付款的 UI 狀態管理應以 DonationPaymentManager 作為主要名稱");
        managerType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }

    /// <summary>
    /// 保護舊供應商名稱的 manager 不再成為可誤用的入口。
    /// 依既定移除相容包裝決策，在真實產品組件查詢舊型別；斷言舊型別不存在，避免因錯誤組件名稱使負向測試假通過。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="DonationPaymentFormModelNamingTests.Donation_payment_manager_is_the_only_churchreport_payment_state_manager"/>
    /// 重複保護舊 manager 不存在的契約；依本次審查指示保留兩個歷史案例，未合併或刪除測試。
    /// </remarks>
    [Fact]
    public void Legacy_qpay_manager_does_not_remain_as_compatibility_alias()
    {
        var managerType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Models.DonationPaymentManager");
        var legacyType = typeof(DonationPaymentManager).Assembly.GetType("ChurchReport.Models.QpayManager");

        managerType.Should().NotBeNull();
        legacyType.Should().BeNull(
            "中性 manager 已是唯一入口，不能重新加入舊供應商相容別名");
    }
}
