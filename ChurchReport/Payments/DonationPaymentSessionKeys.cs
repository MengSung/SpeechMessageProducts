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
    }
}
