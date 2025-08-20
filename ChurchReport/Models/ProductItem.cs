using System;

namespace ChurchReport.Models
{
    /// <summary>
    /// 商品項目類別
    /// </summary>
    public class ProductItem
    {
        /// <summary>
        /// 商品編號
        /// </summary>
        public string id { get; set; }
        
        /// <summary>
        /// 商品名稱
        /// </summary>
        public string name { get; set; }
        
        /// <summary>
        /// 商品單價
        /// </summary>
        public int cost { get; set; }
        
        /// <summary>
        /// 商品數量
        /// </summary>
        public int amount { get; set; }
        
        /// <summary>
        /// 商品小計
        /// </summary>
        public int total { get; set; }
        
        /// <summary>
        /// 商品圖片連結(僅LINEPay線上使用)
        /// </summary>
        public string image_url { get; set; }
    }
}