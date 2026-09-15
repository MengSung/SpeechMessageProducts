// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/DonationLineItemInput.cs
// 所屬區塊：ChurchReport 奉獻付款表單模型層。
// 檔案責任：描述一筆多類別奉獻明細，供 MVC model binding、驗證與建單流程共用。
// 主要型別：DonationLineItemInput
// 主要成員：Category、Amount、Others
// 引用命名空間：無；本 DTO 不持有外部資源或可變共享狀態。
// 維護重點：欄位名稱是前端 Lines[i].Category／Amount／Others 的 POST 契約，不可任意改名。
// 隔離要求：每次請求建立獨立 DTO，不得放入 static、singleton 或跨會友快取，避免資料串流。
// 生命週期：DTO 僅隨單次請求流動，由 model binding 建立，離開請求後由 GC 回收。
// 編碼要求：本檔案維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
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
