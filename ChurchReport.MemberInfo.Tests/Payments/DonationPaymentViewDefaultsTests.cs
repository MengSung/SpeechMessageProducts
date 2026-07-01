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
            Path.Combine(repositoryRoot, "ChurchReport", "Controllers", "DonationPaymentLoginController.cs"));
        var dedicationController = File.ReadAllText(
            Path.Combine(repositoryRoot, "ChurchReport", "Controllers", "DedicationController.cs"));
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
            if (File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 ChurchReport.sln，無法定位測試要檢查的 Controller 原始碼。");
    }
}
