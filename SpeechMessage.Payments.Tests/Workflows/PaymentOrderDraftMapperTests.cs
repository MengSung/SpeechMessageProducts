// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments.Tests/Workflows/PaymentOrderDraftMapperTests.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentOrderDraftMapperTests
// 主要成員：Draft_keeps_product_neutral_order_payer_method_schedule_and_items、Mapper_converts_draft_to_core_create_request_without_host_product_dependencies
// 引用命名空間：FluentAssertions、SpeechMessage.Payments.Workflows、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using SpeechMessage.Payments.Workflows;
using Xunit;

namespace SpeechMessage.Payments.Tests.Workflows;

public sealed class PaymentOrderDraftMapperTests
{
    [Fact]
    public void Draft_keeps_product_neutral_order_payer_method_schedule_and_items()
    {
        var draft = new PaymentOrderDraft
        {
            ProfileName = "MyPayProduction",
            ProductOrderId = "INV-001",
            Amount = 1200m,
            Currency = "TWD",
            Description = "Invoice payment",
            Payer = new PaymentPayerDraft
            {
                Name = "Jane Chen",
                Email = "jane@example.test",
                Phone = "0911222333",
                ExternalPayerId = "customer-001"
            },
            Method = new PaymentMethodSelection
            {
                Method = "CreditCard",
                SubType = "Once",
                ProviderProfileName = "MyPayProduction"
            },
            Schedule = new PaymentScheduleDraft
            {
                IsRecurring = true,
                TotalPeriods = 12,
                PeriodType = "M",
                Frequency = 1,
                StartDate = new DateOnly(2026, 7, 1)
            },
            Items =
            [
                new PaymentLineItemDraft
                {
                    Name = "Invoice line",
                    Quantity = 1,
                    UnitPrice = 1200m,
                    Currency = "TWD",
                    ExternalItemId = "line-001"
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["HostProduct"] = "InvoiceSystem"
            }
        };

        draft.ProfileName.Should().Be("MyPayProduction");
        draft.Payer.ExternalPayerId.Should().Be("customer-001");
        draft.Method.ProviderProfileName.Should().Be("MyPayProduction");
        draft.Schedule.TotalPeriods.Should().Be(12);
        draft.Items.Single().ExternalItemId.Should().Be("line-001");
        draft.Metadata["HostProduct"].Should().Be("InvoiceSystem");
    }

    [Fact]
    public void Mapper_converts_draft_to_core_create_request_without_host_product_dependencies()
    {
        var draft = new PaymentOrderDraft
        {
            ProfileName = "TaishinSandbox",
            ProductOrderId = "MEM-001",
            Amount = 500m,
            Currency = "TWD",
            Description = "Membership fee",
            Payer = new PaymentPayerDraft
            {
                Name = "Member Chen",
                Email = "member@example.test",
                Phone = "0987654321",
                ExternalPayerId = "contact-001"
            },
            Method = new PaymentMethodSelection
            {
                Method = "CreditCard",
                SubType = "Installment",
                ProviderProfileName = "TaishinSandbox"
            },
            Schedule = new PaymentScheduleDraft
            {
                IsRecurring = false
            },
            Items =
            [
                new PaymentLineItemDraft
                {
                    Name = "Membership",
                    Quantity = 1,
                    UnitPrice = 500m,
                    Currency = "TWD",
                    ExternalItemId = "membership-2026"
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["HostProduct"] = "MembershipSystem"
            }
        };

        var request = new PaymentOrderDraftMapper().Map(draft);

        request.ProfileName.Should().Be("TaishinSandbox");
        request.ProductOrderId.Should().Be("MEM-001");
        request.Amount.Should().Be(500m);
        request.Currency.Should().Be("TWD");
        request.Description.Should().Be("Membership fee");
        request.PaymentMethod.Should().Be("CreditCard");
        request.PaymentMethodSubType.Should().Be("Installment");
        request.Customer.Name.Should().Be("Member Chen");
        request.Customer.Email.Should().Be("member@example.test");
        request.Customer.Phone.Should().Be("0987654321");
        request.Items.Should().ContainSingle();
        request.Items[0].Name.Should().Be("Membership");
        request.Metadata["HostProduct"].Should().Be("MembershipSystem");
        request.Metadata["Payer.ExternalPayerId"].Should().Be("contact-001");
        request.Metadata["PaymentMethod.ProviderProfileName"].Should().Be("TaishinSandbox");
        request.Metadata["Schedule.IsRecurring"].Should().Be("false");
        request.Metadata["Item.0.ExternalItemId"].Should().Be("membership-2026");
    }
}
