// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentServiceExtractionTests
// 主要成員：ParseCreditCards_should_convert_legacy_visa_info_into_display_cards、SerializeCreditCards_should_preserve_legacy_crm_storage_format、ResolveSpecialCategory_should_return_name_when_today_is_inside_range、ResolveSpecialCategory_should_return_empty_when_today_is_outside_range、ResolveSpecialCategory_should_ignore_malformed_date_text_without_throwing、ValidateDonationForm_should_reject_empty_amount、ValidateDonationForm_should_require_festival_selection_for_festival_dedication、ClassifyCreatePaymentResult_should_mark_payment_instruction_as_virtual_account、ClassifyCreatePaymentResult_should_return_error_for_credit_card_failure、ConvertStatus_should_map_dedication_booking_options
// 引用命名空間：System、System.IO、ChurchReport.Models、ChurchReport.Services、FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.IO;
using ChurchReport.Models;
using ChurchReport.Services;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class DonationPaymentServiceExtractionTests
{
    [Fact]
    public void ParseCreditCards_should_convert_legacy_visa_info_into_display_cards()
    {
        var cards = DonationCreditCardProfileService.ParseCreditCards(
            "tok_1，1234，5678，2607|tok_2，4321，8765，2512|");

        cards.Should().HaveCount(2);
        cards[0].CCToken.Should().Be("tok_1");
        cards[0].CreditCardNumber.Should().Be("1234-XXXX-5678");
        cards[0].ExpireDate.Should().Be("26/07");
        cards[1].CCToken.Should().Be("tok_2");
        cards[1].CreditCardNumber.Should().Be("4321-XXXX-8765");
        cards[1].ExpireDate.Should().Be("25/12");
    }

    [Fact]
    public void SerializeCreditCards_should_preserve_legacy_crm_storage_format()
    {
        var cards = DonationCreditCardProfileService.ParseCreditCards("tok_1，1234，5678，2607|");

        DonationCreditCardProfileService.SerializeCreditCards(cards)
            .Should()
            .Be("tok_1，1234，5678，2607|");
    }

    [Fact]
    public void ResolveSpecialCategory_should_return_name_when_today_is_inside_range()
    {
        var today = new DateTime(2026, 7, 1);

        DonationPaymentFormBuilder.ResolveSpecialCategory(
                "2026/06/01~2026/07/31,暑期特別奉獻",
                today)
            .Should()
            .Be("暑期特別奉獻");
    }

    [Fact]
    public void ResolveSpecialCategory_should_return_empty_when_today_is_outside_range()
    {
        var today = new DateTime(2026, 8, 1);

        DonationPaymentFormBuilder.ResolveSpecialCategory(
                "2026/06/01~2026/07/31,暑期特別奉獻",
                today)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ResolveSpecialCategory_should_ignore_malformed_date_text_without_throwing()
    {
        var today = new DateTime(2026, 7, 1);

        Action act = () => DonationPaymentFormBuilder.ResolveSpecialCategory(
            "錯誤日期~也不是日期,暑期特別奉獻",
            today);

        act.Should().NotThrow();
        DonationPaymentFormBuilder.ResolveSpecialCategory(
                "錯誤日期~也不是日期,暑期特別奉獻",
                today)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ValidateDonationForm_should_reject_empty_amount()
    {
        var result = DonationPaymentSubmissionService.ValidateDonationForm(new DonationPaymentFormModel
        {
            Amount = 0
        });

        result.Should().Be("未輸入奉獻金額");
    }

    [Fact]
    public void ValidateDonationForm_should_require_festival_selection_for_festival_dedication()
    {
        var result = DonationPaymentSubmissionService.ValidateDonationForm(new DonationPaymentFormModel
        {
            Category = "節期獻金",
            Amount = 100,
            Others = ""
        });

        result.Should().Be("錯誤:沒有選擇節期!");
    }

    [Fact]
    public void ClassifyCreatePaymentResult_should_mark_payment_instruction_as_virtual_account()
    {
        var result = DonationPaymentSubmissionService.ClassifyCreatePaymentResult(
            "銀行代碼 : 807 永豐商業銀行\r\n*** 請依照訊息付款 ***\r\n帳號 : 12345678901234");

        result.Status.Should().Be("1");
        result.PayWay.Should().Be("虛擬帳號");
    }

    [Fact]
    public void ClassifyCreatePaymentResult_should_return_error_for_credit_card_failure()
    {
        var result = DonationPaymentSubmissionService.ClassifyCreatePaymentResult(
            "信用卡繳費失敗! 資料輸入有誤");

        result.Status.Should().Be("2");
        result.Message.Should().Be("信用卡繳費失敗! 資料輸入有誤");
    }

    [Theory]
    [InlineData(100000000, "尚未啟動")]
    [InlineData(100000001, "進行中")]
    [InlineData(100000002, "已結案")]
    [InlineData(100000003, "啟動失敗")]
    [InlineData(100000004, "已取消")]
    [InlineData(999, "尚未啟動")]
    public void ConvertStatus_should_map_dedication_booking_options(int optionSetValue, string expected)
    {
        DonationBookingService.ConvertStatus(optionSetValue).Should().Be(expected);
    }

    [Theory]
    [InlineData(100000000, "現金")]
    [InlineData(100000001, "信用卡")]
    [InlineData(100000002, "ATM轉帳")]
    [InlineData(100000003, "超商付款")]
    [InlineData(100000005, "LinePay")]
    [InlineData(100000006, "銀行轉帳")]
    [InlineData(100000007, "行動支付")]
    [InlineData(100000008, "銀聯卡")]
    [InlineData(999, "未知")]
    public void ConvertPayWay_should_map_fee_payment_options(int optionSetValue, string expected)
    {
        DonationFeeQueryService.ConvertPayWay(optionSetValue).Should().Be(expected);
    }

    [Fact]
    public void DonationPaymentManager_should_delegate_contact_mapping_to_contact_service()
    {
        // 這個測試刻意檢查原始碼結構，而不是測 CRM 行為。
        // 目的在於鎖住本次重構邊界：DonationPaymentManager 只能協調流程，
        // ChurchReport CRM contact 的建立、比對、補欄位細節必須集中在 DonationContactService。
        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
        string contactSection = ExtractSourceSection(
            managerSource,
            "public Entity CreateDonationContact",
            "public void ProcessCreditCard()");

        contactSection.Should().Contain("m_DonationContactService.CreateContact");
        contactSection.Should().Contain("m_DonationContactService.FilterByFullName");
        contactSection.Should().Contain("m_DonationContactService.FilterByNationId");
        contactSection.Should().Contain("m_DonationContactService.FilterByMobile");
        contactSection.Should().Contain("m_DonationContactService.UpdateMissingFields");
        contactSection.Should().NotContain("new Entity(\"contact\")");
        contactSection.Should().NotContain("SetOptionSetAttribute(ref aContactToCreate");
    }

    [Fact]
    public void DonationPaymentManager_should_delegate_key_in_dedication_workflow()
    {
        // 手動奉獻查詢/更新是 ChurchReport 產品流程，manager 應只保留公開入口，
        // CRM 查詢與 JSON payload 組裝要集中在 DonationKeyInDedicationService。
        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
        string keyInSection = ExtractSourceSection(
            managerSource,
            "public async Task<IActionResult> SaveKeyInDedication",
            "public DonationPaymentFormModel SetDedicationFeeList");

        keyInSection.Should().Contain("m_DonationKeyInDedicationService");
        keyInSection.Should().NotContain("QueryDediccationContatsByFetchXml");
    }

    [Fact]
    public void DonationPaymentManager_should_delegate_booking_workflow()
    {
        // 認獻清單與取消認獻會碰 CRM、LINE 與舊金流取消流程，
        // 這些流程應移到 ChurchReport service，不能留在大型 manager 裡。
        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
        string bookingSection = ExtractSourceSection(
            managerSource,
            "public void ProcessDedicationBooking()",
            "public async Task<IActionResult> CreateContact");

        bookingSection.Should().Contain("m_DonationBookingService");
        bookingSection.Should().NotContain("RetrieveDedicationBookingByFetchXml");
        bookingSection.Should().NotContain("new_dedication_booking");
    }

    [Fact]
    public void DonationPaymentManager_should_delegate_contact_creation_numbering_workflow()
    {
        // 查無新人時建立 contact 以及奉獻編號規則是 ChurchReport 資料規則，
        // manager 不應直接建立 contact 或計算 pager 編號。
        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
        string creationSection = ExtractSourceSection(
            managerSource,
            "public async Task<IActionResult> CreateContact",
            "#region 工具區");

        creationSection.Should().Contain("m_DonationContactCreationService");
        creationSection.Should().NotContain("new Entity(\"contact\")");
        creationSection.Should().NotContain("QueryContatsByStartedDedicationNumber");
    }

    [Fact]
    public void DonationPaymentManager_should_delegate_payment_model_assembly()
    {
        // 奉獻表單初始化牽涉 CRM 欄位、OptionSet、信用卡清單與認獻清單，
        // manager 應把組裝細節交給 assembler，避免 UI model 規則散在大型協調器。
        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
        string modelSection = ExtractSourceSection(
            managerSource,
            "public DonationPaymentFormModel SetDonationPaymentModel",
            "public async Task<IActionResult> SaveDonationPaymentDedicationAsync");

        modelSection.Should().Contain("m_DonationPaymentModelAssembler");
        modelSection.Should().NotContain("RetrieveTaskByFetchXml");
        modelSection.Should().NotContain("OptionSetMetadataService");
    }

    [Fact]
    public void DonationPaymentManager_should_delegate_donation_login_contact_workflow()
    {
        // 官網奉獻登入會處理身分證、姓名、同名資料與新 contact 建立，
        // 這是 ChurchReport contact workflow，manager 不應保留大段 CRM 查詢判斷。
        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
        string loginSection = ExtractSourceSection(
            managerSource,
            "public Entity GetDonationPaymentLoginContact",
            "public Entity CreateDonationContact");

        loginSection.Should().Contain("m_DonationLoginContactService");
        loginSection.Should().NotContain("RetrieveContactCollectionByNationId");
        loginSection.Should().NotContain("RetrieveContactCollectionByName");
    }

    [Fact]
    public void DonationPaymentManager_should_delegate_dedication_fee_form_refresh()
    {
        // 奉獻查詢表單刷新包含 contact 欄位投影與 new_fee 查詢，屬於 ChurchReport 產品表單流程。
        // manager 只應保留 public wrapper，讓 service 負責欄位填值與 fee list refresh。
        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
        string feeListSection = ExtractSourceSection(
            managerSource,
            "public DonationPaymentFormModel SetDedicationFeeList(String UserLineId)",
            "#endregion\r\n        #region 電腦網頁或是LINE登入");

        feeListSection.Should().Contain("m_DonationDedicationFeeFormService");
        feeListSection.Should().NotContain("RetrieveContactByLineId");
        feeListSection.Should().NotContain("new DonationFeeQueryService");
    }

    private static string ReadRepositoryFile(params string[] pathSegments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, Path.Combine(pathSegments));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("找不到測試需要讀取的專案原始碼檔案。", Path.Combine(pathSegments));
    }

    private static string ExtractSourceSection(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"原始碼應包含區段起點 {startMarker}");

        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"原始碼應包含區段終點 {endMarker}");

        return source.Substring(start, end - start);
    }
}
