/*
 * 檔案整體說明
 *
 * 目的與定位：一列奉獻明細：一個奉獻類別、該類別金額及補充說明。 MVC 會依照表單欄位名稱 Lines[index].Category、Lines[index].Amount、 Lines[index].Others 
 * 自動繫結；服務層在使用前仍必須自行正規化及驗證， 不可把瀏覽器送來的字串直接當成已可信任的 CRM 或金流資料。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationLineItemInput。
 * 資料／選項欄位包含 DonationLineItemInput、Category、Amount、Others，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Dedication/DonationMultiCategoryTests.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationFeeGroupText.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationGivingHistory.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationLineItemNormalizer.cs 追查目前工作樹中的合作／使用位置。
 */
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
        public string Category { get; set; } = string.Empty;
        public int Amount { get; set; }
        public string Others { get; set; } = string.Empty;
    }
}
