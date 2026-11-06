/*
 * 檔案整體說明
 *
 * 目的與定位：保存單一幸福小組週報的正式ID、聚會日期、地點、時間、週次、主題及BEST明細，並分別追蹤週報與BEST異動供一次提交。
 *
 * 主要功能與流程：
 * 本檔宣告 HappyGroupWeeklyReport。
 * 資料／選項欄位包含 HappyGroupWeeklyReport、HappyGroupName、HappyGroupListEntityId、HappyGroupWeeklyReportId、
 * WeeklyReportModifiedFlag、BestRecordModifiedFlag、MeetingDate、Location、StartTime、EndTime、WeekCounter、Topic、
 * HappyWeeklyReport、ModifiedFlag、BestRecordList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/HappyGroupDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/HappyGroupWeeklyReportListClass.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Views/Home/HappyGroup.cshtml、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs 追查目前工作樹中的合作／使用位置。
 */
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
