/*
 * 檔案整體說明
 *
 * 目的與定位：表示組織樹的一個小組，保存小組／帶領者／區長正式ID、聚會資訊、改派旗標及會友子清單，供名單編輯與批次上傳使用。
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroup。
 * 資料／選項欄位包含 SmallGroup、SmallGroupName、SmallGroupId、SmallGroupLeaderName、SmallGroupLeaderEntityId、RaceLeaderName、
 * RaceLeaderEntityId、SmallGroupModifiedFlag、ContactMemberModifiedFlag、ChangeRaceLeader、ChangeAreaLeader、
 * MeetingDate、Location、ModifiedFlag、ContactMemberList，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Security/LocalReturnUrlTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOA
 * uth.cs、SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    // 小組名單
    public class SmallGroup
    {
        public string SmallGroupName { set; get; }              // 小組名稱
        public string SmallGroupId { set; get; }                // 小組在 CRM 系統中的 EntityId
        public string SmallGroupLeaderName { set; get; }        // 小組長名稱
        public string SmallGroupLeaderEntityId { set; get; }    // 小組長 EntityId
        public string RaceLeaderName { set; get; }              // 區長名稱
        public string RaceLeaderEntityId { set; get; }          // 區長 EntityId

        public bool SmallGroupModifiedFlag { get; set; }
        public bool ContactMemberModifiedFlag { get; set; }

        public string ChangeRaceLeader { set; get; }          // 換區長
        public string ChangeAreaLeader { set; get; }          // 換區牧

        //public List<String> RaceLeaderArray { get; set; } = new List<string>();     //換區長要用到的區長清單
        //public List<String> AreaLeaderArray { get; set; } = new List<string>();     //換區牧要用到的區牧清單


        public DateTime MeetingDate { get; set; }                   // 小組聚會日期
        public string Location { set; get; }                        // 小組聚會地點

        public bool ModifiedFlag { set; get; } = false;             // 設定小組是否有被修改過
        public List<ContactMember> ContactMemberList { set; get; }  // 小組成員名單
    }
}
