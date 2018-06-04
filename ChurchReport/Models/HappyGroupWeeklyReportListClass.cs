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

        public bool DirtyFlag { set; get; } = false;

        public List<HappyGroupWeeklyReport> HappyGroupWeeklyReportList { set; get; } // 週報清單
    }
}
