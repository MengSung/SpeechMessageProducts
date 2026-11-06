/*
 * 檔案整體說明
 *
 * 目的與定位：判斷目前登入者是否可以看到「奉獻管理」與「奉獻稽核」導覽按鈕。 這個類別只處理導覽列權限判斷，不讀取 CRM、不修改畫面模型，也不呼叫金流或 LINE。 這樣做的原因是 _Layout.cshtml
 * 會在很多頁面被共用，如果導覽列依賴 DonationPaymentManager.m_DonationPaymentFormModel.IsAOfficeWorker，就會發生： 1. 使用者剛登入或停留在非奉獻頁時，
 * 奉獻付款表單尚未初始化。 2. IsAOfficeWorker 仍是預設 false。 3. Layout 因此誤判，隱藏原本應該出現的「奉獻管理」按鈕。 所以導覽列權限應直接由「登入者 CRM contact
 * 的教會職稱」推導。 目前沿用原本 DonationPaymentModelAssembler.IsAccountingWorker 的產品規則： 教會職稱包含「會計」即可看到奉獻管理入口。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationNavigationAccessResolver。
 * 主要操作入口包含 CanAccessDonationManagement。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs 追查目前工作樹中的合作／使用位置。
 */
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
