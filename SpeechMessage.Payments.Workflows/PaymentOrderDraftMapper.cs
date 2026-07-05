// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments.Workflows/PaymentOrderDraftMapper.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class PaymentOrderDraftMapper
// 主要成員：Map、AddIfNotEmpty
// 引用命名空間：SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
