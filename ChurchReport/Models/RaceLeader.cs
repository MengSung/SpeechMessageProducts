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


        public string ChangeRaceLeader { get; set; } // 換區
        public string ChangeAreaLeader { get; set; } // 換牧區

        //public List<String> RaceLeaderArray { get; set; } = new List<string>();     //換區要用到的區長清單
        //public List<String> AreaLeaderArray { get; set; } = new List<string>();     //換牧區要用到的區牧清單

        public bool DirtyFlag { set; get; } = true;

        public List<SmallGroup> SmallGroupList { set; get; }    // 小組清單
    }
}
