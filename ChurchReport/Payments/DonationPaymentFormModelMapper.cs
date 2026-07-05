// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/DonationPaymentFormModelMapper.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentFormModelMapper
// 主要成員：Map、IsRecurringPayWay、ResolveDeductTotalPeriods、FirstNonEmpty
// 引用命名空間：System、System.Collections.Generic、System.Globalization、System.Linq、ChurchReport.Models、SpeechMessage.Payments.Workflows
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChurchReport.Models;
using SpeechMessage.Payments.Workflows;

namespace ChurchReport.Payments;

/// <summary>
/// 將 ChurchReport 的奉獻付款表單狀態轉成可重用的 PaymentOrderDraft。
///
/// 這個 mapper 是刻意存在的邊界層：
/// - 左邊是 ChurchReport 產品語言，例如奉獻者、奉獻類別、奉獻編號、定期定額期數。
/// - 右邊是 SpeechMessage.Payments.Workflows 的中性語言，例如付款人、付款項目、付款方法、付款排程。
///
/// 為什麼不要直接把 DonationPaymentFormModel 丟進金流核心？
/// 1. DonationPaymentFormModel 只有 ChurchReport 懂，未來的建設維修系統或協會會員系統不會有奉獻編號。
/// 2. 金流核心只應該知道「付款訂單草稿」，不應該知道 ViewBag、CRM、LINE 或奉獻分類。
/// 3. 透過 mapper 轉換後，未來產品只要做自己的 mapper，就可以共用 PaymentOrderDraft 與後續 gateway 流程。
///
/// 這個類別只做純資料轉換，不呼叫外部服務、不讀 appsettings、不存資料庫。
/// 這樣測試容易，責任也清楚。
/// </summary>
public sealed class DonationPaymentFormModelMapper
{
    /// <summary>
    /// 將 ChurchReport 表單模型映射成中性付款訂單草稿。
    ///
    /// profileName 是金流 profile 名稱，例如 JesusTest、MyPayProduction、TaishinSandbox。
    /// productOrderId 是產品端訂單號，例如 C202607010001。
    /// productEntityId 是 ChurchReport 自己的 CRM fee 或 dedication booking entity id。
    ///
    /// 注意：這三個值由呼叫端提供，因為它們通常要依照實際流程產生；
    /// mapper 不應該自己決定 profile，也不應該自己建立 CRM record。
    /// </summary>
    public PaymentOrderDraft Map(
        DonationPaymentFormModel formModel,
        string profileName,
        string productOrderId,
        string productEntityId)
    {
        ArgumentNullException.ThrowIfNull(formModel);

        var paymentMethod = ResolvePaymentMethod(formModel.PayWay);
        var isRecurring = IsRecurringPayWay(formModel.PayWay);
        var itemName = FirstNonEmpty(formModel.Category, "Donation payment");
        var payerName = FirstNonEmpty(formModel.FullName, formModel.DedicationNumber, productOrderId);
        var amount = Convert.ToDecimal(formModel.Amount, CultureInfo.InvariantCulture);

        return new PaymentOrderDraft
        {
            ProfileName = profileName ?? string.Empty,
            ProductOrderId = productOrderId ?? string.Empty,
            Amount = amount,
            Currency = "TWD",
            Description = $"{itemName}-{payerName}",
            Payer = new PaymentPayerDraft
            {
                Name = payerName,
                Phone = formModel.Mobile ?? string.Empty,
                ExternalPayerId = formModel.SelectedContactId ?? string.Empty
            },
            Method = new PaymentMethodSelection
            {
                Method = paymentMethod.Method,
                SubType = paymentMethod.SubType
            },
            Schedule = new PaymentScheduleDraft
            {
                IsRecurring = isRecurring,
                TotalPeriods = isRecurring ? ResolveDeductTotalPeriods(formModel.DeductTotalNumber) : 0,
                PeriodType = isRecurring ? "M" : string.Empty,
                Frequency = isRecurring ? 1 : 0
            },
            Items =
            [
                new PaymentLineItemDraft
                {
                    Name = itemName,
                    Quantity = 1,
                    UnitPrice = amount,
                    Currency = "TWD",
                    ExternalItemId = productEntityId ?? string.Empty
                }
            ],
            Metadata = BuildMetadata(formModel, productEntityId)
        };
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(
        DonationPaymentFormModel formModel,
        string productEntityId)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Donation.Category"] = formModel.Category ?? string.Empty,
            ["Donation.PayWay"] = formModel.PayWay ?? string.Empty,
            ["Donation.DedicationNumber"] = formModel.DedicationNumber ?? string.Empty,
            ["Donation.Explain"] = formModel.Explain ?? string.Empty,
            ["Donation.ProductEntityId"] = productEntityId ?? string.Empty,
            ["Payment.CreditCardToken"] = formModel.SelectedCreditCard ?? string.Empty
        };

        return metadata;
    }

    private static (string Method, string SubType) ResolvePaymentMethod(string payWay)
    {
        return payWay switch
        {
            "ATM轉帳/匯款" => ("A", string.Empty),
            "行動支付" => ("M", string.Empty),
            "LinePay" => ("L", string.Empty),
            "信用卡定期定額(每個月)" => ("C", "REGULAR"),
            "銀聯卡" => ("C", "CUP"),
            _ => ("C", "ONE")
        };
    }

    private static bool IsRecurringPayWay(string payWay)
    {
        return string.Equals(payWay, "信用卡定期定額(每個月)", StringComparison.Ordinal);
    }

    private static int ResolveDeductTotalPeriods(string deductTotalNumber)
    {
        if (string.IsNullOrWhiteSpace(deductTotalNumber))
        {
            return 12;
        }

        var digits = new string(deductTotalNumber.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var periods) && periods > 0
            ? periods
            : 12;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
