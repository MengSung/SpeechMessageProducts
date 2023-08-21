using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class RaceLeader
    {
        public string RaceLeaderName { set; get; }              // 族系族長名稱

        public string LoginUserId { set; get; }                 // 登入的使用者 Entity Id

        public string RaceLeaderEntityId { set; get; }          // 族系族長的 Entity Id


        public string ChangeRaceLeader { get; set; } // 換族系
        public string ChangeAreaLeader { get; set; } // 換上代族系

        //public List<String> RaceLeaderArray { get; set; } = new List<string>();     //換族系要用到的族系族長清單
        //public List<String> AreaLeaderArray { get; set; } = new List<string>();     //換上代族系要用到的上代族系族長清單

        public bool DirtyFlag { set; get; } = true;

        public List<SmallGroup> SmallGroupList { set; get; }    // 小組清單
    }
}
