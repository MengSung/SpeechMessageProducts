/*
 * 檔案整體說明
 *
 * 目的與定位：組合一個幸福小組的名單ID、帶領者、異動旗標與歷次週報集合，提供畫面分組及後續下載上傳的資料邊界。
 *
 * 主要功能與流程：
 * 本檔宣告 HappyGroupWeeklyReportListClass。
 * 資料／選項欄位包含 HappyGroupWeeklyReportListClass、HappyGroupName、LoginUserId、ListEntityId、SpiritLeaderList、DirtyFlag、
 * HappyGroupWeeklyReportList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/HappyGroupDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/HappyGroupListClass.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class HappyGroupWeeklyReportListClass
    {
        public string HappyGroupName { set; get; } // 幸福小組名稱

        public string LoginUserId { set; get; } // 幸福小組名單登入的使用者 Entity Id

        public string ListEntityId { set; get; } // 幸福小組名單的 Entity Id

        public string SpiritLeaderList { set; get; }// 幸福小組屬靈認養者

        public bool DirtyFlag { set; get; } = true;

        public List<HappyGroupWeeklyReport> HappyGroupWeeklyReportList { set; get; } // 週報清單
    }
}
