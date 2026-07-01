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
