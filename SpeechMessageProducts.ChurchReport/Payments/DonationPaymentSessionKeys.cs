/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 奉獻付款流程使用的 ASP.NET Session key。 注意：這些 key 屬於 ChurchReport 的網站流程狀態，不屬於可重用金流核心。 金流核心只處理 
 * provider 協定與標準化付款結果；登入者、CRM contact、 LINE 通知與畫面狀態都必須留在產品專案。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentSessionKeys。
 * - 網頁奉獻登入成功後保存的 CRM contact id。 目的： AJAX 登入成功後會經過 browser redirect 再進入奉獻頁。若中途 DonationPaymentManager 的 
 * memory-cache key 因 Session 指紋或建立時間差異而分裂， 奉獻頁仍可用這個穩定的 Session 值重新讀取 contact， 重新建立姓名、奉獻編號、信用卡清單與認獻清單。
 * - LINE LIFF 登入成功後由伺服器保存的已驗證 LINE user id。 收費清單與付款頁的 route segment 可由瀏覽器自行修改，因此只能在 Session 中存在同一個、已完成 CRM 
 * contact 驗證的值時接受該 route。 此 key 不保存姓名、contact 或其他個資，並隨 Session 到期而失效。
 * 資料／選項欄位包含 DonationPaymentSessionKeys、WebLoginContactId、LineUserId，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DonationPaymentLoginController.cs 追查目前工作樹中的合作／使用位置。
 */
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
