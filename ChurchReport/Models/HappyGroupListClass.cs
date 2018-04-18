using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class HappyGroupListClass
    {
        // 幸福小組名單登入的使用者 Entity Id
        public string LoginUserId { set; get; } 

        // 一個人會開多個幸福小組
        public List<HappyGroupWeeklyReportListClass> HappyGroupWeeklyReportListClass { set; get; } // 幸福小組出席紀錄單清單
    }
}
