using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Converts host-product payment drafts into the provider-neutral create request used by the gateway.
/// This mapper is intentionally thin: validation and product enrichment stay in the host product.
/// </summary>
public sealed class PaymentOrderDraftMapper
{
    public PaymentCreateRequest Map(PaymentOrderDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var metadata = new Dictionary<string, string>(draft.Metadata, StringComparer.Ordinal);
        AddIfNotEmpty(metadata, "Payer.ExternalPayerId", draft.Payer.ExternalPayerId);
        AddIfNotEmpty(metadata, "PaymentMethod.ProviderProfileName", draft.Method.ProviderProfileName);
        metadata["Schedule.IsRecurring"] = draft.Schedule.IsRecurring ? "true" : "false";

        if (draft.Schedule.TotalPeriods > 0)
        {
            metadata["Schedule.TotalPeriods"] = draft.Schedule.TotalPeriods.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        AddIfNotEmpty(metadata, "Schedule.PeriodType", draft.Schedule.PeriodType);

        if (draft.Schedule.Frequency > 0)
        {
            metadata["Schedule.Frequency"] = draft.Schedule.Frequency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (draft.Schedule.StartDate.HasValue)
        {
            metadata["Schedule.StartDate"] = draft.Schedule.StartDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        var items = draft.Items
            .Select((item, index) =>
            {
                AddIfNotEmpty(metadata, $"Item.{index}.ExternalItemId", item.ExternalItemId);

                return new PaymentLineItem
                {
                    Name = item.Name,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Currency = item.Currency
                };
            })
            .ToArray();

        return new PaymentCreateRequest
        {
            ProfileName = draft.ProfileName,
            ProductOrderId = draft.ProductOrderId,
            Amount = draft.Amount,
            Currency = draft.Currency,
            Description = draft.Description,
            PaymentMethod = draft.Method.Method,
            PaymentMethodSubType = draft.Method.SubType,
            Customer = new PaymentCustomer
            {
                Name = draft.Payer.Name,
                Email = draft.Payer.Email,
                Phone = draft.Payer.Phone
            },
            Items = items,
            Metadata = metadata
        };
    }

    private static void AddIfNotEmpty(IDictionary<string, string> metadata, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }
}
