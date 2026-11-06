/*
 * 檔案整體說明
 *
 * 目的與定位：承接舊付款頁交易建立後的回傳欄位，提供既有轉址／結果呈現所需資料；回傳值不是付款成功的證明，最終狀態應依經驗證的金流回呼及查詢結果。
 *
 * 主要功能與流程：
 * 本檔宣告 PayPageResponse。
 * 資料／選項欄位包含 PayPageResponse、code、msg、uid、key、url、transaction_id、order_no，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 */
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
