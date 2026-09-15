// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationFeeGroupText.cs
// 所屬區塊：奉獻付款服務層；集中處理同一筆付款的群組文字與金流明細。
// 檔案責任：提供總額、類別摘要、商品名稱、PaymentLineItem 與 ATM 標籤文字的純函式。
// 主要型別：DonationFeeGroupText
// 生命週期／資源：不建立 Session、CRM 連線、背景工作、timer 或 static 可變快取；回傳值由呼叫端持有。
// 隔離要求：輸入只讀取目前請求的明細快照，不跨會友或跨請求保存任何資料。
// 外部契約：ATM 標籤文字及 PaymentLineItem 欄位不可任意改動，前端解析與 provider 均依賴它們。
// 編碼要求：本檔案維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Services
{
    /// <summary>同一筆奉獻付款群組的可重用、無副作用文字組裝器。</summary>
    public static class DonationFeeGroupText
    {
        public const int ProviderProductNameMaxLength = 60;

        public static int Total(IReadOnlyList<DonationLineItemInput> lines) => lines?.Sum(x => x?.Amount ?? 0) ?? 0;

        public static string CategorySummary(IReadOnlyList<DonationLineItemInput> lines)
        {
            if (lines == null || lines.Count == 0) return string.Empty;
            if (lines.Count == 1) return lines[0]?.Category ?? string.Empty;
            return string.Join("、", lines.Where(x => x != null).Select(x => $"{x.Category} {x.Amount:N0}元"));
        }

        public static string ProviderProductName(IReadOnlyList<DonationLineItemInput> lines, string fullName)
        {
            var names = (lines ?? Array.Empty<DonationLineItemInput>()).Where(x => x != null)
                .Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var full = $"{(names.Count == 0 ? "奉獻" : string.Join("+", names))}-{fullName}";
            if (full.Length <= ProviderProductNameMaxLength) return full;
            var compact = $"{(names.FirstOrDefault() ?? "奉獻")}等{names.Count}項-{fullName}";
            return compact.Length <= ProviderProductNameMaxLength ? compact : compact.Substring(0, ProviderProductNameMaxLength);
        }

        public static IReadOnlyList<PaymentLineItem> ProviderItems(IReadOnlyList<DonationLineItemInput> lines) =>
            (lines ?? Array.Empty<DonationLineItemInput>()).Where(x => x != null).Select(x => new PaymentLineItem
            {
                Name = x.Category ?? string.Empty, Quantity = 1, UnitPrice = x.Amount, Currency = "TWD"
            }).ToList();

        /// <summary>
        /// 產生 ATM 顯示與 LINE 共用文字。標籤名稱刻意維持既有契約，
        /// 以確保 DonationPaymentView 的 parser 與「複製虛擬帳號」功能不被破壞。
        /// </summary>
        public static (string LineMessage, string HtmlMessage) AtmInfo(string fullName, IReadOnlyList<DonationLineItemInput> lines, string atmPayNo, string expireDate)
        {
            var nl = Environment.NewLine;
            var message = $"姓名 : {fullName}{nl}名稱 : {CategorySummary(lines)}{nl}金額 : {Total(lines)}元{nl}付款到期日: {expireDate}{nl}*** 請依照訊息付款 ***{nl}銀行代碼 : 807 永豐商業銀行{nl}分行代號 : 021 台北分行{nl}帳號     : {atmPayNo}{nl}戶名     : 其他應付款-代收-網路收款";
            return (message, message.Replace(nl, "<br/>"));
        }
    }
}
