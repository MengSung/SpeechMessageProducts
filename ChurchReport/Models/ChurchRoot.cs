using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ChurchRoot
    {
        public string LoginUserId { set; get; }                 // 登入的使用者 Entity Id

        public bool DirtyFlag { set; get; } = true;

        public List<AreaLeader> AreaLeaderList { set; get; }    // 上代族系族長清單
    }
}
