/*
 * 檔案整體說明
 *
 * 目的與定位：包裝地圖元件需要的MapData標記清單，供資料組裝端一次交付畫面；同座標或同文字不代表同一業務記錄，集合的身分驗證由來源組裝端負責。
 *
 * 主要功能與流程：
 * 本檔宣告 MapDataList。
 * 資料／選項欄位包含 MapDataList、m_MapDataList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class MapDataList
    {
        public List<MapData> m_MapDataList { get; set; }
    }
}
