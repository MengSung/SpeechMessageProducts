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
        public string RaceLeaderName { set; get; }              // 族系族長名稱
        public string RaceLeaderEntityId { set; get; }          // 族系族長 EntityId

        public bool SmallGroupModifiedFlag { get; set; }
        public bool ContactMemberModifiedFlag { get; set; }

        public string ChangeRaceLeader { set; get; }          // 換族系族長
        public string ChangeAreaLeader { set; get; }          // 換上代族系族長

        //public List<String> RaceLeaderArray { get; set; } = new List<string>();     //換族系要用到的族系族長清單
        //public List<String> AreaLeaderArray { get; set; } = new List<string>();     //換上代族系要用到的上代族系族長清單


        public DateTime MeetingDate { get; set; }                   // 小組聚會日期
        public string Location { set; get; }                        // 小組聚會地點

        public bool ModifiedFlag { set; get; } = false;             // 設定小組是否有被修改過
        public List<ContactMember> ContactMemberList { set; get; }  // 小組成員名單
    }
}
