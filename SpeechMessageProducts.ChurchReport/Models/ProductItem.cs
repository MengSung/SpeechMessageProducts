/*
 * 檔案整體說明
 *
 * 目的與定位：描述付款頁的單一商品項目，提供名稱、數量與金額等舊交易欄位；它是序列化／表單契約，產品授權與總額核對應在建立付款的服務完成。
 *
 * 主要功能與流程：
 * 本檔宣告 ProductItem。
 * 資料／選項欄位包含 ProductItem、id、name、cost、amount、total、image_url，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/PayPage.cs 追查目前工作樹中的合作／使用位置。
 */
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
