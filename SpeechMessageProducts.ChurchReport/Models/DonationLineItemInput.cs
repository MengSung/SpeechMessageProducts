/*
 * 檔案整體說明
 *
 * 目的與定位：一列奉獻明細：一個奉獻類別、該類別金額及補充說明。 MVC 會依照表單欄位名稱 Lines[index].Category、Lines[index].Amount、 Lines[index].Others
 * 自動繫結；服務層在使用前仍必須自行正規化及驗證， 不可把瀏覽器送來的字串直接當成已可信任的 CRM 或金流資料。
 *
 * 主要功能與流程：
 * Category 綁定正式奉獻類別 Guid，Amount 與 Others 保存該列輸入；服務端解析完成後可在
 * 本次建單操作內部附帶 OfferingCategory，不把它當作瀏覽器輸入或序列化欄位。
 * 資料／選項欄位包含 DonationLineItemInput、Category、Amount、Others，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * ResolvedCategory 只由付款流程在該次 request 驗證後設定，不得從 model binding 填入或跨請求保存。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationFeeGroupText.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationGivingHistory.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationLineItemNormalizer.cs、
 * SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessageProducts.GoodShepherd.Core.Offerings;

namespace ChurchReport.Models
{
    /// <summary>
    /// 一列奉獻明細：一個奉獻類別、該類別金額及補充說明。
    /// MVC 會依照表單欄位名稱 Lines[index].Category、Lines[index].Amount、
    /// Lines[index].Others 自動繫結；服務層在使用前仍必須自行正規化及驗證，
    /// 不可把瀏覽器送來的字串直接當成已可信任的 CRM 或金流資料。
    /// </summary>
    public sealed class DonationLineItemInput
    {
        /// <summary>表單送入的奉獻類別正式資料庫 Guid 字串；服務端仍須依登入教會重新驗證。</summary>
        public string Category { get; set; } = string.Empty;
        /// <summary>此明細金額；正負與整體付款規則由送出服務檢查。</summary>
        public int Amount { get; set; }
        /// <summary>此明細的使用者補充文字，僅在授權成功後由產品流程寫入 CRM。</summary>
        public string Others { get; set; } = string.Empty;

        /// <summary>歷史投影的正式類別 ID；只供本次操作及測試辨識，不參與模型繫結或序列化。</summary>
        internal System.Guid? CategoryId { get; set; }

        /// <summary>僅在當次付款操作完成伺服器解析後設定，不參與 model binding 或 JSON 序列化。</summary>
        internal OfferingCategory? ResolvedCategory { get; set; }
    }
}
