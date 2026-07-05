// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationPaymentFormBuilder.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationPaymentFormBuilder
// 主要成員：ResolveSpecialCategory、ParseDateTime
// 引用命名空間：System、System.Globalization
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
