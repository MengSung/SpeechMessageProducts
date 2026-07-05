// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/IChurchReportLineBindingNotificationService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IChurchReportLineBindingNotificationService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Services;

/// <summary>
/// ChurchReport 專用的 LINE 綁定通知服務。
/// Controller 只需要表達「目前流程要通知使用者綁定 LINE」，
/// 不應該知道 LINE profile 怎麼查、綁定網址怎麼組、訊息最後怎麼透過 workflow 發送。
/// </summary>
public interface IChurchReportLineBindingNotificationService
{
    /// <summary>
    /// 發送 ChurchReport LINE 帳號綁定提示。
    /// 這個流程會查詢 LINE 使用者顯示名稱，組出 ChurchReport 的綁定頁 URL，
    /// 再透過共用 LINE notification workflow 發送文字訊息。
    /// </summary>
    /// <param name="lineUserId">LINE user id。</param>
    /// <param name="cancellationToken">ASP.NET request 取消權杖。</param>
    Task NotifyLineBindingAsync(string lineUserId, CancellationToken cancellationToken = default);
}

