// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Caching/CacheKeys.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CacheKeys、class Expiration
// 主要成員：MultiGroupList、WeeklyReport、ChartData、Members、Dropdown、IntegrateData、ContactByAccount、ContactByLineId、ContactById、PresentRecord
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace ChurchReport.Services.Caching
{
    /// <summary>
    /// 快取鍵常數與建構器
    /// Phase 2.2: 統一管理所有快取鍵格式，避免魔術字串
    ///
    /// 快取策略說明:
    /// - MultiGroupList: 多小組清單，絕對過期 30 分鐘
    /// - WeeklyReport: 週報資料，絕對過期 15 分鐘
    /// - ChartData: 圖表資料，滑動過期 10 分鐘
    /// - Members: 會員清單，絕對過期 10 分鐘
    /// - Dropdown: 下拉選單，絕對過期 60 分鐘
    /// </summary>
    public static class CacheKeys
    {
        #region 快取前綴

        /// <summary>多小組清單前綴</summary>
        public const string MultiGroupListPrefix = "MultiGroupList_";

        /// <summary>週報資料前綴</summary>
        public const string WeeklyReportPrefix = "WeeklyReport_";

        /// <summary>圖表資料前綴</summary>
        public const string ChartDataPrefix = "ChartData_";

        /// <summary>會員清單前綴</summary>
        public const string MembersPrefix = "Members_";

        /// <summary>下拉選單前綴</summary>
        public const string DropdownPrefix = "Dropdown_";

        /// <summary>小組整合資料前綴</summary>
        public const string IntegrateDataPrefix = "IntegrateData_";

        /// <summary>聯絡人資料前綴</summary>
        public const string ContactPrefix = "Contact_";

        /// <summary>出席記錄前綴</summary>
        public const string PresentRecordPrefix = "PresentRecord_";

        /// <summary>清單資料前綴</summary>
        public const string ListPrefix = "List_";

        #endregion

        #region 快取鍵建構方法

        /// <summary>
        /// 建立多小組清單快取鍵
        /// </summary>
        /// <param name="account">帳號</param>
        /// <param name="date">日期</param>
        public static string MultiGroupList(string account, DateTime date)
            => $"{MultiGroupListPrefix}{account}_{date:yyyyMMdd}";

        /// <summary>
        /// 建立週報資料快取鍵
        /// </summary>
        /// <param name="listId">清單ID</param>
        /// <param name="date">日期</param>
        public static string WeeklyReport(string listId, DateTime date)
            => $"{WeeklyReportPrefix}{listId}_{date:yyyyMMdd}";

        /// <summary>
        /// 建立圖表資料快取鍵
        /// </summary>
        /// <param name="listId">清單ID</param>
        public static string ChartData(string listId)
            => $"{ChartDataPrefix}{listId}";

        /// <summary>
        /// 建立會員清單快取鍵
        /// </summary>
        /// <param name="listId">清單ID</param>
        public static string Members(string listId)
            => $"{MembersPrefix}{listId}";

        /// <summary>
        /// 建立下拉選單快取鍵
        /// </summary>
        /// <param name="dropdownType">下拉選單類型</param>
        public static string Dropdown(string dropdownType)
            => $"{DropdownPrefix}{dropdownType}";

        /// <summary>
        /// 建立小組整合資料快取鍵
        /// </summary>
        /// <param name="listId">清單ID</param>
        /// <param name="date">日期</param>
        public static string IntegrateData(string listId, DateTime date)
            => $"{IntegrateDataPrefix}{listId}_{date:yyyyMMdd}";

        /// <summary>
        /// 建立聯絡人快取鍵（依帳號密碼）
        /// </summary>
        /// <param name="account">帳號</param>
        public static string ContactByAccount(string account)
            => $"{ContactPrefix}Account_{account}";

        /// <summary>
        /// 建立聯絡人快取鍵（依 LineId）
        /// </summary>
        /// <param name="lineId">Line ID</param>
        public static string ContactByLineId(string lineId)
            => $"{ContactPrefix}LineId_{lineId}";

        /// <summary>
        /// 建立聯絡人快取鍵（依 EntityId）
        /// </summary>
        /// <param name="contactId">聯絡人 ID</param>
        public static string ContactById(Guid contactId)
            => $"{ContactPrefix}Id_{contactId}";

        /// <summary>
        /// 建立出席記錄快取鍵
        /// </summary>
        /// <param name="weeklyReportId">週報ID</param>
        public static string PresentRecord(string weeklyReportId)
            => $"{PresentRecordPrefix}{weeklyReportId}";

        /// <summary>
        /// 建立清單實體快取鍵
        /// </summary>
        /// <param name="listId">清單ID</param>
        public static string ListEntity(string listId)
            => $"{ListPrefix}{listId}";

        #endregion

        #region 快取過期時間常數

        /// <summary>
        /// 快取過期時間設定
        /// </summary>
        public static class Expiration
        {
            /// <summary>多小組清單：絕對過期 30 分鐘</summary>
            public static readonly TimeSpan MultiGroupList = TimeSpan.FromMinutes(30);

            /// <summary>週報資料：絕對過期 15 分鐘</summary>
            public static readonly TimeSpan WeeklyReport = TimeSpan.FromMinutes(15);

            /// <summary>圖表資料：滑動過期 10 分鐘</summary>
            public static readonly TimeSpan ChartData = TimeSpan.FromMinutes(10);

            /// <summary>會員清單：絕對過期 10 分鐘</summary>
            public static readonly TimeSpan Members = TimeSpan.FromMinutes(10);

            /// <summary>下拉選單：絕對過期 60 分鐘</summary>
            public static readonly TimeSpan Dropdown = TimeSpan.FromMinutes(60);

            /// <summary>小組整合資料：絕對過期 15 分鐘</summary>
            public static readonly TimeSpan IntegrateData = TimeSpan.FromMinutes(15);

            /// <summary>聯絡人資料：絕對過期 30 分鐘</summary>
            public static readonly TimeSpan Contact = TimeSpan.FromMinutes(30);

            /// <summary>出席記錄：絕對過期 10 分鐘</summary>
            public static readonly TimeSpan PresentRecord = TimeSpan.FromMinutes(10);

            /// <summary>清單實體：絕對過期 30 分鐘</summary>
            public static readonly TimeSpan ListEntity = TimeSpan.FromMinutes(30);

            /// <summary>短期快取：5 分鐘</summary>
            public static readonly TimeSpan Short = TimeSpan.FromMinutes(5);

            /// <summary>中期快取：15 分鐘</summary>
            public static readonly TimeSpan Medium = TimeSpan.FromMinutes(15);

            /// <summary>長期快取：60 分鐘</summary>
            public static readonly TimeSpan Long = TimeSpan.FromMinutes(60);
        }

        #endregion
    }
}
