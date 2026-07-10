// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentViewDefaultsTests
// 主要成員：New_qpay_model_has_donation_category_and_payment_method_defaults、Qpay_model_can_restore_required_form_defaults_after_reused_state_is_cleared、Qpay_model_reports_when_web_login_donor_identity_must_be_restored、Dedication_donation_payment_view_action_is_async_so_line_user_initialization_completes_before_rendering、Web_login_flow_persists_contact_id_and_donation_payment_view_uses_it_to_restore_missing_model_state、FindRepositoryRoot
// 引用命名空間：ChurchReport.Controllers、ChurchReport.Models、ChurchReport.Payments、FluentAssertions、Microsoft.AspNetCore.Mvc、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Controllers;
using ChurchReport.Models;
using ChurchReport.Payments;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 鎖定奉獻頁初始資料的安全預設值。
/// 奉獻頁可能在 LINE 使用者資料或 CRM OptionSet 尚未載入完成前先產生 View，
/// 因此 ViewModel 本身必須提供基本奉獻類別與付款方式，避免下拉選單空白。
/// </summary>
public sealed class DonationPaymentViewDefaultsTests
{
    [Fact]
    public void New_qpay_model_has_donation_category_and_payment_method_defaults()
    {
        var model = new DonationPaymentFormModel();

        model.Category.Should().Be("十一奉獻");
        model.PayWay.Should().Be("信用卡");
        model.DedicationCategoryList.Should().Contain("十一奉獻");
        model.OtherCategoryArray.Should().NotBeNull();
        model.SpecialCategoryArray.Should().NotBeNull();
    }

    [Fact]
    public void Qpay_model_can_restore_required_form_defaults_after_reused_state_is_cleared()
    {
        var model = new DonationPaymentFormModel
        {
            Category = "",
            PayWay = "",
            DedicationCategoryList = new List<string>(),
            OtherCategoryArray = null!,
            SpecialCategoryArray = null!,
            CreditCardList = null!,
            DedicationFeeList = null!,
            DedicationBookingList = null!
        };

        model.EnsureFormDefaults();

        model.Category.Should().Be("十一奉獻");
        model.PayWay.Should().Be("信用卡");
        model.DedicationCategoryList.Should().Contain("十一奉獻");
        model.OtherCategoryArray.Should().NotBeNull();
        model.SpecialCategoryArray.Should().NotBeNull();
        model.CreditCardList.Should().NotBeNull();
        model.DedicationFeeList.Should().NotBeNull();
        model.DedicationBookingList.Should().NotBeNull();
    }

    [Theory]
    [InlineData("", "D001", true)]
    [InlineData("胡夢嵩", "", true)]
    [InlineData("胡夢嵩", "D001", false)]
    public void Qpay_model_reports_when_web_login_donor_identity_must_be_restored(
        string fullName,
        string dedicationNumber,
        bool expected)
    {
        var model = new DonationPaymentFormModel
        {
            FullName = fullName,
            DedicationNumber = dedicationNumber
        };

        model.NeedsDonorIdentityRestore().Should().Be(expected);
    }

    [Fact]
    public void Dedication_donation_payment_view_action_is_async_so_line_user_initialization_completes_before_rendering()
    {
        var method = typeof(DedicationController).GetMethod(nameof(DedicationController.DonationPaymentView));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<IActionResult>),
            "DonationPaymentView 會呼叫非同步 LINE 初始化流程，必須 await 完成後才 render 奉獻表單");
    }

    [Fact]
    public void Web_login_flow_persists_contact_id_and_donation_payment_view_uses_it_to_restore_missing_model_state()
    {
        var repositoryRoot = FindRepositoryRoot();
        var loginController = File.ReadAllText(
            Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport", "Controllers", "DonationPaymentLoginController.cs"));
        var dedicationController = File.ReadAllText(
            Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport", "Controllers", "DedicationController.cs"));
        var contactIdSessionKeyName = nameof(DonationPaymentSessionKeys.WebLoginContactId);

        loginController.Should().Contain(contactIdSessionKeyName,
            "web login 成功後必須把 CRM contact id 存入 ASP.NET Session，避免 redirect 後讀到空的 DonationPaymentManager");
        loginController.Should().Contain("Session.SetString",
            "contact id 應該存在 session 這種跨 redirect 穩定狀態，而不是只留在 memory-cache manager");
        dedicationController.Should().Contain("RestoreWebLoginDonationPaymentModel",
            "DonationPaymentView render 前必須能用 session contact id 重新建立姓名、奉獻編號與信用卡清單");
        dedicationController.Should().Contain(contactIdSessionKeyName);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 ChurchReport.sln，無法定位測試要檢查的 Controller 原始碼。");
    }
}
