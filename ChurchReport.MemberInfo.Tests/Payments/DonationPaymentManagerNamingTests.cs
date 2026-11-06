/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 ChurchReport 的奉獻付款 UI 狀態管理器已改用中性名稱。 這組測試不觸發 CRM 或 LINE，只鎖定型別邊界，確認中性 manager 存在，且舊供應商別名不再形成第二個入口。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentManagerNamingTests。
 * - 保護付款 UI 的中性入口仍為 MVC Controller。 以完整型別名稱查詢真實產品組件，模擬型別被刪除或移到錯誤組件；斷言型別存在且可作為 Controller，不建立任何 Session 或外部連線。
 * - 保護舊供應商名稱的 manager 不再成為可誤用的入口。 依既定移除相容包裝決策，在真實產品組件查詢舊型別；斷言舊型別不存在，避免因錯誤組件名稱使負向測試假通過。
 * 測試案例入口包含 New_donation_payment_manager_exists_as_primary_ui_payment_state_manager、
 * Legacy_qpay_manager_does_not_remain_as_compatibility_alias。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
