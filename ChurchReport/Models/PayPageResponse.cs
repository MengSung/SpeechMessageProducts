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
    }
}