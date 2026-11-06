/*
 * 檔案整體說明
 *
 * 目的與定位：定義地圖標記的座標與提示內容形狀，讓前端地圖元件取得location及tooltip；它是顯示投影，不負責地理位置查詢或聯絡人的存取授權。
 *
 * 主要功能與流程：
 * 本檔宣告 MapData、tooltip。
 * 資料／選項欄位包含 MapData、location、tooltip、text、isShown，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/ListManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/MapDataList.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class MapData
    {
        //public String PresentRecordEntityId { get; set; }
        public String location { get; set; }
        public tooltip tooltip { get; set; }
    }
    public class tooltip
    {
        public String text { get; set; }
        public bool isShown { get; set; }
    }
}
