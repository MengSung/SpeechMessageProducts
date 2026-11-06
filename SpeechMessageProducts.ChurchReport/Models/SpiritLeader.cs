/*
 * 檔案整體說明
 *
 * 目的與定位：表示帶領者選單單筆ID與名稱，保持選單顯示與CRM記錄身分分離；相同姓名的不同ID仍應保留為獨立候選。
 *
 * 主要功能與流程：
 * 本檔宣告 SpiritLeader。
 * 資料／選項欄位包含 SpiritLeader、ID、Name，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs、
 * SpeechMessageProducts.ChurchReport/Models/SpiritLeadeList.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class SpiritLeader
    {
        public int ID { get; set; }
        public string Name { get; set; }
    }
}
