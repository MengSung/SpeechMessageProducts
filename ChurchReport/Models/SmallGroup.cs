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
