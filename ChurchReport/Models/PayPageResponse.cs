// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/PayPageResponse.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class PayPageResponse
// 主要成員：code、msg、uid、key、url、transaction_id、order_no
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace ChurchReport.Models
{
    /// <summary>
    /// PayPage 金流交易回傳結果類別
    /// </summary>
    public class PayPageResponse
    {
        /// <summary>
        /// 交易回傳碼
        /// </summary>
        public string code { get; set; }

        /// <summary>
        /// 回傳訊息
        /// </summary>
        public string msg { get; set; }

        /// <summary>
        /// 訂單之交易流水號(交易訂單/票券服務訂單/儲值訂單)
        /// </summary>
        public string uid { get; set; }

        /// <summary>
        /// 交易驗証碼
        /// </summary>
        public string key { get; set; }

        /// <summary>
        /// 交易網址
        /// </summary>
        public string url { get; set; }

        /// <summary>
        /// 交易編號 (TSPG 專用)
        /// </summary>
        public string transaction_id { get; set; }

        /// <summary>
        /// 訂單編號 (TSPG 專用)
        /// </summary>
        public string order_no { get; set; }
    }
}