/*
 * 檔案整體說明
 *
 * 目的與定位：同一筆奉獻付款群組的可重用、無副作用文字組裝器。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationFeeGroupText。
 * - 產生 ATM 顯示與 LINE 共用文字。標籤名稱刻意維持既有契約， 以確保 DonationPaymentView 的 parser 與「複製虛擬帳號」功能不被破壞。
 * 主要操作入口包含 Total、CategorySummary、ProviderProductName、ProviderItems。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Dedication/DonationMultiCategoryTests.cs、
 * SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Pay
 * mentProcessing.cs 追查目前工作樹中的合作／使用位置。
 */
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
