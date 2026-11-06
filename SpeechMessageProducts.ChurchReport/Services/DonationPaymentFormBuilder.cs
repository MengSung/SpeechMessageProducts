/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 奉獻付款表單的產品層組裝服務。 這個類別只放 ChurchReport 奉獻頁面會用到的表單規則，例如特別奉獻日期區間。 它不屬於 
 * SpeechMessage.Payments.AspNetCore ，因為「節期獻金、特別奉獻」 是 ChurchReport 的產品語言，未來建設公司維修、協會會員或發票收款系統不應被迫引用。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentFormBuilder。
 * - 解析 CRM task 內用來設定特別奉獻項目的字串。 格式為「yyyy/MM/dd~yyyy/MM/dd,顯示名稱」；只有指定日期落在區間內才回傳顯示名稱。
 * - 奉獻設定資料目前可能使用多種日期格式；集中解析可避免每個呼叫端各自猜格式。 CRM 內的 task 文字可由人工維護，若日期被誤填，這裡要退回現在時間而不是丟例外， 
 * 避免整個奉獻付款頁面因單筆特別奉獻設定格式錯誤而無法載入。
 * 主要操作入口包含 ResolveSpecialCategory、ParseDateTime。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationPaymentModelAssembler.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Globalization;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻付款表單的產品層組裝服務。
    ///
    /// 這個類別只放 ChurchReport 奉獻頁面會用到的表單規則，例如特別奉獻日期區間。
    /// 它不屬於 <c>SpeechMessage.Payments.AspNetCore</c>，因為「節期獻金、特別奉獻」
    /// 是 ChurchReport 的產品語言，未來建設公司維修、協會會員或發票收款系統不應被迫引用。
    /// </summary>
    public sealed class DonationPaymentFormBuilder
    {
        /// <summary>
        /// 解析 CRM task 內用來設定特別奉獻項目的字串。
        /// 格式為「yyyy/MM/dd~yyyy/MM/dd,顯示名稱」；只有指定日期落在區間內才回傳顯示名稱。
        /// </summary>
        public static string ResolveSpecialCategory(string? specialCategory, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(specialCategory))
            {
                return string.Empty;
            }

            var categoryParts = specialCategory.Split(',');
            if (categoryParts.Length != 2)
            {
                return string.Empty;
            }

            var dateParts = categoryParts[0].Split('~');
            if (dateParts.Length != 2)
            {
                return string.Empty;
            }

            var startDate = ParseDateTime(dateParts[0]).Date;
            var endDateExclusive = ParseDateTime(dateParts[1]).Date.AddDays(1);

            return startDate < today.Date && today.Date < endDateExclusive
                ? categoryParts[1]
                : string.Empty;
        }

        /// <summary>
        /// 奉獻設定資料目前可能使用多種日期格式；集中解析可避免每個呼叫端各自猜格式。
        /// CRM 內的 task 文字可由人工維護，若日期被誤填，這裡要退回現在時間而不是丟例外，
        /// 避免整個奉獻付款頁面因單筆特別奉獻設定格式錯誤而無法載入。
        /// </summary>
        public static DateTime ParseDateTime(string dateString)
        {
            var formats = new[] { "yyyy/MM/dd", "yyyy-MM-dd", "yyyyMMdd" };
            if (DateTime.TryParseExact(dateString, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactResult))
            {
                return exactResult;
            }

            if (DateTime.TryParse(dateString, CultureInfo.CurrentCulture, DateTimeStyles.None, out var cultureResult))
            {
                return cultureResult;
            }

            return DateTime.Now;
        }
    }
}
