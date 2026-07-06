// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Donation/DonationNavigationAccessResolver.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationNavigationAccessResolver
// 主要成員：CanAccessDonationManagement
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
