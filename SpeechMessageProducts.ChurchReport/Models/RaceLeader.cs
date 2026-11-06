/*
 * 檔案整體說明
 *
 * 目的與定位：表示組織樹中的區長節點，連接正式ID、登入工作識別、上層區牧異動與下層小組集合；異動旗標指示待提交候選，不能直接視為CRM已保存。
 *
 * 主要功能與流程：
 * 本檔宣告 RaceLeader。
 * 資料／選項欄位包含 RaceLeader、RaceLeaderName、LoginUserId、RaceLeaderEntityId、ChangeRaceLeader、ChangeAreaLeader、DirtyFlag、
 * SmallGroupList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/ListManagementController.cs、
 * SpeechMessageProducts.ChurchReport/Models/AreaLeader.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Views/Home/ListManagement.cshtml、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class RaceLeader
    {
        public string RaceLeaderName { set; get; }              // 區長名稱

        public string LoginUserId { set; get; }                 // 登入的使用者 Entity Id

        public string RaceLeaderEntityId { set; get; }          // 區長的 Entity Id


        public string ChangeRaceLeader { get; set; } // 換區長
        public string ChangeAreaLeader { get; set; } // 換區牧

        //public List<String> RaceLeaderArray { get; set; } = new List<string>();     //換區長要用到的區長清單
        //public List<String> AreaLeaderArray { get; set; } = new List<string>();     //換區牧要用到的區牧清單

        public bool DirtyFlag { set; get; } = true;

        public List<SmallGroup> SmallGroupList { set; get; }    // 小組清單
    }
}
