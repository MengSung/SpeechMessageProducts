// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/Authentication/SessionData.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class SessionData
// 主要成員：LoginContact、Account、Password、FullName、LoginType、DisplayViewType、ActiveListId、UserType、HasHappyGroup、HasFeeData
// 引用命名空間：Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// Session 資料模型
    /// </summary>
    public class SessionData
    {
        /// <summary>
        /// 登入的連絡人實體
        /// </summary>
        public Entity LoginContact { get; set; }

        /// <summary>
        /// 帳號
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// 密碼
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 全名
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// 登入類型
        /// </summary>
        public string LoginType { get; set; }

        /// <summary>
        /// 顯示檢視類型
        /// </summary>
        public string DisplayViewType { get; set; }

        /// <summary>
        /// 活躍清單 ID
        /// </summary>
        public string ActiveListId { get; set; }

        /// <summary>
        /// 使用者類型
        /// </summary>
        public string UserType { get; set; }

        /// <summary>
        /// 是否有幸福小組
        /// </summary>
        public bool HasHappyGroup { get; set; }

        /// <summary>
        /// 是否有繳費資料
        /// </summary>
        public bool HasFeeData { get; set; }
    }
}
