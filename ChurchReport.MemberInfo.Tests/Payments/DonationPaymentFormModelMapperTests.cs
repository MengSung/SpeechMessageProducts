// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelMapperTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentFormModelMapperTests
// 主要成員：Mapper_converts_churchreport_donation_form_to_neutral_payment_order_draft、Mapper_marks_monthly_recurring_credit_card_forms_as_recurring_schedule
// 引用命名空間：ChurchReport.Models、ChurchReport.Payments、FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Payments;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 測試 ChurchReport 奉獻付款表單如何轉成共用金流 workflows 專案的 PaymentOrderDraft。
///
/// 這個測試鎖定的是「資料邊界」，不是銀行付款本身：
/// - ChurchReport 仍然可以使用奉獻者、奉獻類別、奉獻編號等產品語言。
/// - 跨出 ChurchReport 之後，資料要變成 PaymentOrderDraft 這種中性合約。
/// - PaymentOrderDraft 不能知道 CRM、LINE、ViewBag、Controller 或任何 ChurchReport 專屬流程。
///
/// 這樣未來建設公司維修系統、協會會員系統、發票收款系統也可以建立自己的 FormModel，
/// 再用相同概念轉成 PaymentOrderDraft，而不是複製 ChurchReport 的奉獻表單模型。
/// </summary>
public sealed class DonationPaymentFormModelMapperTests
{
    [Fact]
    public void Mapper_converts_churchreport_donation_form_to_neutral_payment_order_draft()
    {
        var formModel = new DonationPaymentFormModel
        {
            FullName = "王小明",
            Mobile = "0912345678",
            DedicationNumber = "D001",
            Amount = 1200,
            Category = "十一奉獻",
            PayWay = "信用卡",
            DeductTotalNumber = "12期",
            SelectedCreditCard = "card-token",
            SelectedContactId = "contact-001",
            Explain = "主日奉獻"
        };
        var mapper = new DonationPaymentFormModelMapper();

        var draft = mapper.Map(
            formModel,
            profileName: "JesusTest",
            productOrderId: "C202607010001",
            productEntityId: "fee-001");

        draft.ProfileName.Should().Be("JesusTest");
        draft.ProductOrderId.Should().Be("C202607010001");
        draft.Amount.Should().Be(1200m);
        draft.Currency.Should().Be("TWD");
        draft.Description.Should().Be("十一奉獻-王小明");
        draft.Payer.Name.Should().Be("王小明");
        draft.Payer.Phone.Should().Be("0912345678");
        draft.Payer.ExternalPayerId.Should().Be("contact-001");
        draft.Method.Method.Should().Be("C");
        draft.Method.SubType.Should().Be("ONE");
        draft.Items.Should().ContainSingle(item =>
            item.Name == "十一奉獻" &&
            item.Quantity == 1 &&
            item.UnitPrice == 1200m &&
            item.Currency == "TWD" &&
            item.ExternalItemId == "fee-001");
        draft.Metadata.Should().Contain("Donation.Category", "十一奉獻");
        draft.Metadata.Should().Contain("Donation.PayWay", "信用卡");
        draft.Metadata.Should().Contain("Donation.DedicationNumber", "D001");
        draft.Metadata.Should().Contain("Donation.Explain", "主日奉獻");
        draft.Metadata.Should().Contain("Payment.CreditCardToken", "card-token");
    }

    [Fact]
    public void Mapper_marks_monthly_recurring_credit_card_forms_as_recurring_schedule()
    {
        var formModel = new DonationPaymentFormModel
        {
            FullName = "王小明",
            Amount = 500,
            Category = "建堂奉獻",
            PayWay = "信用卡定期定額(每個月)",
            DeductTotalNumber = "12期"
        };
        var mapper = new DonationPaymentFormModelMapper();

        var draft = mapper.Map(
            formModel,
            profileName: "JesusTest",
            productOrderId: "B202607010001",
            productEntityId: "booking-001");

        draft.Method.Method.Should().Be("C");
        draft.Method.SubType.Should().Be("REGULAR");
        draft.Schedule.IsRecurring.Should().BeTrue();
        draft.Schedule.TotalPeriods.Should().Be(12);
        draft.Schedule.PeriodType.Should().Be("M");
        draft.Schedule.Frequency.Should().Be(1);
    }
}
