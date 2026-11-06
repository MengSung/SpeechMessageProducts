/*
 * 檔案整體說明
 *
 * 目的與定位：表示一個主日週次的主日／小組人數統計，週報ID連回權威記錄；它是圖表投影，不在屬性設定時回寫CRM或執行統計查詢。
 *
 * 主要功能與流程：
 * 本檔宣告 ChartData。
 * 資料／選項欄位包含 ChartData、WeeklyReportEntityId、SundayDate、SundayNumber、SmallNumber，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs、
 * SpeechMessageProducts.ChurchReport/Models/ChartDataList.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs、
 * SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ChartData
    {
        public String WeeklyReportEntityId { get; set; }
        public String SundayDate { get; set; }
        public int SundayNumber { get; set; }
        public int SmallNumber { get; set; }
    }
}
