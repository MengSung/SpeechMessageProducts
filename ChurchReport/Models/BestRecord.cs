using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    // 幸福小組出席紀錄單
    public class BestRecord
    {
        public bool BestModifiedFlag { get; set; }

        public string BestRecordEntityId { set; get; } // 幸福小組出席紀錄單在 CRM 系統中的 EntityId
        public string BestRecordId { set; get; } // 幸福小組出席紀錄單在回報陣列中的Id
        public string BestRecordParentId { set; get; } // 幸福小組出席紀錄單的上一層周報的Id
        public string FullName { get; set; }
        public string MobilePhone { get; set; }
        public bool Present { get; set; }
        public bool Decision { get; set; }
        public string Note { get; set; }
        public string BestLeader { get; set; } // 屬靈認領者
    }
}
