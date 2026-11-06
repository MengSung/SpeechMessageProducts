/*
 * 檔案整體說明
 *
 * 目的與定位：將產品中性訂單草稿映射為gateway的付款建立請求，保持欄位與metadata對齊；本層刻意是薄映射，產品授權、價格驗證與補資料由上游負責。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentOrderDraftMapper。
 * 主要操作入口包含 Map、AddIfNotEmpty。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
