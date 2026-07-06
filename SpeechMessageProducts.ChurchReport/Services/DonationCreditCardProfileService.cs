// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationCreditCardProfileService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationCreditCardProfileService
// 主要成員：ParseCreditCards、SerializeCreditCards、FormatExpireDate
// 引用命名空間：System、System.Collections.Generic、System.Linq、ChurchReport.Models
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻者信用卡保存格式的產品層服務。
    ///
    /// 這個服務刻意留在 ChurchReport 專案，因為它處理的是 CRM contact 欄位
    /// <c>new_visa_info</c> 的歷史儲存格式，不是銀行 provider 的信用卡協定。
    /// 抽出這個類別的目的，是讓 DonationPaymentManager 不再直接解析以「全形逗號 + |」
    /// 組成的字串，降低大檔案的責任範圍，也讓格式轉換可以用單元測試保護。
    /// </summary>
    public sealed class DonationCreditCardProfileService
    {
        private const char CardSeparator = '|';
        private const char FieldSeparator = '，';

        /// <summary>
        /// 將 CRM contact 的 <c>new_visa_info</c> 字串轉成畫面可顯示的信用卡清單。
        /// 舊格式為：CCToken，左四碼，右四碼，YYMM|CCToken，左四碼，右四碼，YYMM|
        /// </summary>
        public static List<CreditCard> ParseCreditCards(string? visaInfo)
        {
            var cards = new List<CreditCard>();

            if (string.IsNullOrWhiteSpace(visaInfo))
            {
                return cards;
            }

            foreach (var rawCard in visaInfo.Split(CardSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = rawCard.Split(FieldSeparator);
                if (fields.Length != 4)
                {
                    continue;
                }

                cards.Add(new CreditCard
                {
                    CCToken = fields[0],
                    LeftCardNumber = fields[1],
                    RightCardNumber = fields[2],
                    CreditCardNumber = fields[1] + "-XXXX-" + fields[2],
                    ExpireDate = FormatExpireDate(fields[3])
                });
            }

            return cards;
        }

        /// <summary>
        /// 將畫面信用卡清單轉回 CRM 歷史格式。
        /// 這裡不重新設計格式，因為既有 CRM 欄位與舊資料仍依賴這個字串契約。
        /// </summary>
        public static string SerializeCreditCards(IEnumerable<CreditCard> cards)
        {
            if (cards == null)
            {
                return string.Empty;
            }

            return string.Concat(cards.Select(card =>
                card.CCToken + FieldSeparator +
                card.LeftCardNumber + FieldSeparator +
                card.RightCardNumber + FieldSeparator +
                (card.ExpireDate ?? string.Empty).Replace("/", string.Empty) +
                CardSeparator));
        }

        /// <summary>
        /// 將 YYMM 轉成畫面顯示用 YY/MM。格式不符時回傳原字串，避免因舊資料髒值中斷整頁。
        /// </summary>
        public static string FormatExpireDate(string? expiredDate)
        {
            if (string.IsNullOrWhiteSpace(expiredDate) || expiredDate.Length < 4)
            {
                return expiredDate ?? string.Empty;
            }

            return expiredDate.Substring(0, 2) + "/" + expiredDate.Substring(2, 2);
        }
    }
}
