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
