using System;

namespace ChurchReport.Services.Donation
{
    /// <summary>
    /// 判斷目前登入者是否可以看到「奉獻管理」與「奉獻稽核」導覽按鈕。
    ///
    /// 這個類別只處理導覽列權限判斷，不讀取 CRM、不修改畫面模型，也不呼叫金流或 LINE。
    /// 這樣做的原因是 _Layout.cshtml 會在很多頁面被共用，如果導覽列依賴
    /// DonationPaymentManager.m_DonationPaymentFormModel.IsAOfficeWorker，就會發生：
    /// 1. 使用者剛登入或停留在非奉獻頁時，奉獻付款表單尚未初始化。
    /// 2. IsAOfficeWorker 仍是預設 false。
    /// 3. Layout 因此誤判，隱藏原本應該出現的「奉獻管理」按鈕。
    ///
    /// 所以導覽列權限應直接由「登入者 CRM contact 的教會職稱」推導。
    /// 目前沿用原本 DonationPaymentModelAssembler.IsAccountingWorker 的產品規則：
    /// 教會職稱包含「會計」即可看到奉獻管理入口。
    /// </summary>
    public static class DonationNavigationAccessResolver
    {
        /// <summary>
        /// 回傳登入者是否具備奉獻管理導覽權限。
        /// </summary>
        /// <param name="churchJobTitle">
        /// CRM contact.new_church_jobtitle 的文字值。
        /// 這裡只接收已取出的字串，避免 resolver 依賴 CRM SDK 或 ToolUtility。
        /// </param>
        public static bool CanAccessDonationManagement(string churchJobTitle)
        {
            var jobTitle = (churchJobTitle ?? string.Empty).Trim();
            return jobTitle.Contains("會計", StringComparison.Ordinal);
        }
    }
}
