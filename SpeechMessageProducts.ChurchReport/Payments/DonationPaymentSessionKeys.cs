// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/DonationPaymentSessionKeys.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentSessionKeys
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace ChurchReport.Payments
{
    /// <summary>
    /// ChurchReport 奉獻付款流程使用的 ASP.NET Session key。
    ///
    /// 注意：這些 key 屬於 ChurchReport 的網站流程狀態，不屬於可重用金流核心。
    /// 金流核心只處理 provider 協定與標準化付款結果；登入者、CRM contact、
    /// LINE 通知與畫面狀態都必須留在產品專案。
    /// </summary>
    public static class DonationPaymentSessionKeys
    {
        /// <summary>
        /// 網頁奉獻登入成功後保存的 CRM contact id。
        ///
        /// 目的：
        /// AJAX 登入成功後會經過 browser redirect 再進入奉獻頁。若中途
        /// DonationPaymentManager 的 memory-cache key 因 Session 指紋或建立時間差異而分裂，
        /// 奉獻頁仍可用這個穩定的 Session 值重新讀取 contact，
        /// 重新建立姓名、奉獻編號、信用卡清單與認獻清單。
        /// </summary>
        public const string WebLoginContactId = "_DonationPaymentWebLoginContactId";

        /// <summary>
        /// LINE LIFF 登入成功後由伺服器保存的已驗證 LINE user id。
        /// 收費清單與付款頁的 route segment 可由瀏覽器自行修改，因此只能在
        /// Session 中存在同一個、已完成 CRM contact 驗證的值時接受該 route。
        /// 此 key 不保存姓名、contact 或其他個資，並隨 Session 到期而失效。
        /// </summary>
        public const string LineUserId = "_DonationPaymentLineUserId";

    }
}
