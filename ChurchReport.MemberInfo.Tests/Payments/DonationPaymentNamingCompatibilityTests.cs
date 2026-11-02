// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentNamingCompatibilityTests
// 主要成員：Old_qpay_card_controller_should_not_remain_as_csharp_alias、New_payment_return_controller_exists_after_rename
// 引用命名空間：FluentAssertions、Microsoft.AspNetCore.Mvc、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
