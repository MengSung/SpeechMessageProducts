using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class AreaLeader
    {
        public string AreaLeaderName { set; get; }              // 區牧名稱

        public string LoginUserId { set; get; }                 // 登入的使用者 Entity Id

        public string AreaLeaderEntityId { set; get; }          // 區牧的 Entity Id

        public bool DirtyFlag { set; get; } = true;

        public List<RaceLeader> RaceLeaderList { set; get; }    // 區長清單
    }
}
