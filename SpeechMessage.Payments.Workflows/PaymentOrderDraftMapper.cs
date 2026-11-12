/*
 * 檔案整體說明
 *
 * 目的與定位：將 workflow 訂單草稿映射為金流核心付款請求；保留上游產品欄位並將草稿 decimal／幣別驗證後轉成 Money。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentOrderDraftMapper。
 * Map 建立 Money-first 總額與商品明細，AddIfNotEmpty 僅投影非空產品中繼資料。
 *
 * 使用邊界與維護：
 * 草稿中的金額與幣別僅是產品輸入；Money 建構會拒絕白名單外幣別。mapper 不保留草稿、使用者或共享狀態，也不執行 I/O。
 * 修改金額契約須同步核對 PaymentCreateRequest、PaymentLineItem 與 workflow mapper 測試。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// 將產品付款草稿轉成 Gateway 使用的 provider-neutral 建立請求。
/// 產品授權與補資料留在產品層；金額欄位在此經 Money 建構式驗證後才進入共用核心。
/// </summary>
public sealed class PaymentOrderDraftMapper
{
    /// <summary>將單次產品訂單草稿的金額、商品、付款者及中繼資料投影為金流核心 DTO。</summary>
    /// <param name="draft">由呼叫端建立並擁有的付款草稿，不會被 mapper 保存或修改。</param>
    /// <returns>以 Money 表達總額與每項單價的中立付款請求。</returns>
    /// <exception cref="ArgumentException">任一草稿幣別不在 Money 白名單時由 Money 建構式拒絕。</exception>
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
                    Price = new SpeechMessage.Localization.Money(item.UnitPrice, item.Currency)
                };
            })
            .ToArray();

        return new PaymentCreateRequest
        {
            ProfileName = draft.ProfileName,
            ProductOrderId = draft.ProductOrderId,
            Total = new SpeechMessage.Localization.Money(draft.Amount, draft.Currency),
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
