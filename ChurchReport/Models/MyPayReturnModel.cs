using System;
using System.ComponentModel.DataAnnotations;

namespace ChurchReport.Models
{
    /// <summary>
    /// 高鉅金流 PayPage 交易完成回傳資訊模型
    /// 根據高鉅金流官方文檔的回傳欄位規格
    /// </summary>
    public class MyPayReturnModel
    {
        /// <summary>
        /// 交易狀態 (1:成功, 0:失敗)
        /// </summary>
        [Required]
        public string state { get; set; }

        /// <summary>
        /// 回傳訊息
        /// </summary>
        [Required]
        public string msg { get; set; }

        /// <summary>
        /// 商店訂單編號 (我們傳送的收費單/認獻單ID)
        /// </summary>
        [Required]
        public string order_id { get; set; }

        /// <summary>
        /// 商店代號
        /// </summary>
        [Required]
        public string store_uid { get; set; }

        /// <summary>
        /// 金流平台交易單號
        /// </summary>
        [Required]
        public string transaction_id { get; set; }

        /// <summary>
        /// 簽名驗證碼
        /// </summary>
        [Required]
        public string hash { get; set; }

        /// <summary>
        /// 交易金額 (選填)
        /// </summary>
        public int? cost { get; set; }

        /// <summary>
        /// 付款人姓名 (選填)
        /// </summary>
        public string user_name { get; set; }

        /// <summary>
        /// 其他可能的回傳欄位 (根據實際文檔可能有更多欄位)
        /// </summary>
        public string user_real_name { get; set; }

        public string user_phone { get; set; }

        public string user_email { get; set; }

        public string currency { get; set; }

        public string pay_type { get; set; }

        public string invoice_number { get; set; }
    }
}