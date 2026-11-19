/*
 * 檔案整體說明
 *
 * 目的與定位：同一筆奉獻付款群組的可重用、無副作用文字組裝器。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationFeeGroupText。
 * - 產生 ATM 顯示、LINE 與金流商品文字。已解析類別一律用伺服器名稱顯示，表單 Guid 不進入使用者文案；
 *   舊純文字呼叫端維持原輸出，確保相容流程與 DonationPaymentView 的 parser 不被破壞。
 * 主要操作入口包含 Total、CategorySummary、ProviderProductName、ProviderItems。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs
 * 追查目前工作樹中的合作／使用位置。
 * P6-07公開邊界：DonationFeeGroupText改為產品內部宣告；外部輸出與既有資源清理契約不變。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Services
{
    /// <summary>同一筆奉獻付款群組的可重用、無副作用文字組裝器。</summary>
    /// <remarks>P6-07限於本產品內部使用；既有資料形狀、呼叫順序與資源所有權不變，不作為跨組件API。</remarks>
    internal static class DonationFeeGroupText
    {
        public const int ProviderProductNameMaxLength = 60;

        public static int Total(IReadOnlyList<DonationLineItemInput> lines) => lines?.Sum(x => x?.Amount ?? 0) ?? 0;

        public static string CategorySummary(IReadOnlyList<DonationLineItemInput> lines)
        {
            if (lines == null || lines.Count == 0) return string.Empty;
            if (lines.Count == 1) return DisplayCategoryName(lines[0]?.Category, lines[0]?.ResolvedCategory);
            return string.Join("、", lines.Where(x => x != null)
                .Select(x => $"{DisplayCategoryName(x.Category, x.ResolvedCategory)} {x.Amount:N0}元"));
        }

        public static string ProviderProductName(IReadOnlyList<DonationLineItemInput> lines, string fullName)
        {
            var names = (lines ?? Array.Empty<DonationLineItemInput>()).Where(x => x != null)
                .Select(x => DisplayCategoryName(x.Category, x.ResolvedCategory))
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var full = $"{(names.Count == 0 ? "奉獻" : string.Join("+", names))}-{fullName}";
            if (full.Length <= ProviderProductNameMaxLength) return full;
            var compact = $"{(names.FirstOrDefault() ?? "奉獻")}等{names.Count}項-{fullName}";
            return compact.Length <= ProviderProductNameMaxLength ? compact : compact.Substring(0, ProviderProductNameMaxLength);
        }

        public static IReadOnlyList<PaymentLineItem> ProviderItems(IReadOnlyList<DonationLineItemInput> lines) =>
            (lines ?? Array.Empty<DonationLineItemInput>()).Where(x => x != null).Select(x => new PaymentLineItem
            {
                Name = DisplayCategoryName(x.Category, x.ResolvedCategory), Quantity = 1, UnitPrice = x.Amount, Currency = "TWD"
            }).ToList();

        /// <summary>
        /// 將本次伺服器解析出的類別名稱用於外部文案；未解析的舊純文字呼叫端保持原值，
        /// 但格式正確的 Guid 不得洩漏為奉獻者可見標籤。
        /// </summary>
        /// <param name="categoryValue">表單綁定值，正式流程中為資料庫 Guid。</param>
        /// <param name="resolvedCategory">本次授權教會清單解析出的類別，未解析時為 null。</param>
        /// <returns>資料庫類別名稱、相容的舊文字，或無法安全顯示時的空字串。</returns>
        internal static string DisplayCategoryName(string categoryValue, OfferingCategory resolvedCategory)
        {
            if (resolvedCategory != null)
            {
                return resolvedCategory.Name ?? string.Empty;
            }

            return Guid.TryParseExact(categoryValue, "D", out _)
                ? string.Empty
                : categoryValue ?? string.Empty;
        }

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
