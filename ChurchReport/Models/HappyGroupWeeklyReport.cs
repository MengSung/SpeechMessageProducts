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
