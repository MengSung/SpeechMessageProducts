/*
 * 檔案整體說明
 *
 * 目的與定位：驗證中性付款回傳 Controller 存在且保有 MVC 繼承關係，同時禁止舊 C# Controller 別名回流。 測試以真實產品組件查詢型別；既有 URL 的相容性由產品路由負責，不在此呼叫銀行回傳流程。
 *  全程只讀型別資訊，不建立控制器、Session、付款或外部連線。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentNamingCompatibilityTests。
 * - 保護舊外部 URL 的相容性不得靠舊 C# Controller 別名實作。 在真實產品組件查詢舊控制器名稱；斷言型別不存在，既有路由由中性控制器承接。
 * - 保護付款回傳的中性 MVC 入口。 在實際產品組件查詢完整名稱，偵測控制器遺失或錯誤移出 MVC 邊界；斷言存在且繼承 Controller。
 * 測試案例入口包含 Old_qpay_card_controller_should_not_remain_as_csharp_alias、
 * New_payment_return_controller_exists_after_rename。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證中性付款回傳 Controller 存在且保有 MVC 繼承關係，同時禁止舊 C# Controller 別名回流。
/// 測試以真實產品組件查詢型別；既有 URL 的相容性由產品路由負責，不在此呼叫銀行回傳流程。
/// 全程只讀型別資訊，不建立控制器、Session、付款或外部連線。
/// </summary>
public sealed class DonationPaymentNamingCompatibilityTests
{
    /// <summary>
    /// 保護舊外部 URL 的相容性不得靠舊 C# Controller 別名實作。
    /// 在真實產品組件查詢舊控制器名稱；斷言型別不存在，既有路由由中性控制器承接。
    /// </summary>
    [Fact]
    public void Old_qpay_card_controller_should_not_remain_as_csharp_alias()
    {
        // 既有金流回傳 URL 由中性控制器的路由維持相容。
        // 這是外部金流設定與舊連結的相容層，不代表新程式還應該以 QPay 命名為主。
        var legacyType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Controllers.QPayCardController");

        legacyType.Should().BeNull();
    }

    /// <summary>
    /// 保護付款回傳的中性 MVC 入口。
    /// 在實際產品組件查詢完整名稱，偵測控制器遺失或錯誤移出 MVC 邊界；斷言存在且繼承 Controller。
    /// </summary>
    [Fact]
    public void New_payment_return_controller_exists_after_rename()
    {
        // 這個測試描述目標狀態：
        // 新的主要 controller 名稱應該描述「付款回傳端點」，而不是描述特定金流供應商。
        var newType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Controllers.PaymentReturnController");

        newType.Should().NotBeNull("新的主要回傳 Controller 應該使用 provider-neutral 命名");
        newType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
    }
}
