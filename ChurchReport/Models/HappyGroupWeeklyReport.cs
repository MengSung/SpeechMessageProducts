// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/HappyGroupWeeklyReport.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class HappyGroupWeeklyReport
// 主要成員：HappyGroupName、HappyGroupListEntityId、HappyGroupWeeklyReportId、WeeklyReportModifiedFlag、BestRecordModifiedFlag、MeetingDate、Location、StartTime、EndTime、WeekCounter
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    // 幸福小組週報
    public class HappyGroupWeeklyReport
    {
        public string HappyGroupName { set; get; }              // 幸福小組名稱
        public string HappyGroupListEntityId { set; get; }      // 在 CRM 系統中幸福小組名單的 EntityId
        public string HappyGroupWeeklyReportId { set; get; }    // 幸福小組週報的在回報陣列中的Id ，也會是 幸福小組週報在 CRM 系統中的 EntityId

        public bool WeeklyReportModifiedFlag { get; set; }
        public bool BestRecordModifiedFlag { get; set; }

        public DateTime MeetingDate { get; set; }               // 聚會日期
        public string Location { set; get; }                    // 聚會地點

        public string StartTime { get; set; }                   // 開始時間
        public string EndTime { get; set; }                     // 結束時間
        public string WeekCounter { set; get; }                 // 週次
        public string Topic { set; get; }                       // 主題
        public string HappyWeeklyReport { set; get; }           // 幸福小組日誌回報

        public bool ModifiedFlag { set; get; } = false;         // 設定週報是否有被修改過
        public List<BestRecord> BestRecordList { set; get; }    // 幸福小組出席紀錄單清單
    }
}
