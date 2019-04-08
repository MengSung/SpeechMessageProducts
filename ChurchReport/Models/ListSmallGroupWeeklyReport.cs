using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ListSmallGroupWeeklyReport
    {
        public String ListEntityId { get; set; }
        public String ListEntityName { get; set; }
        public String LoginType { get; set; }
        public String SmallGroupLeaderContactId { get; set; }
        public String SmallGroupLeaderFullName { get; set; }
        public DateTime SundayPrayers { get; set; } // 小組日期
        public String SundayPeriod { get; set; }   // 提醒小組長回報的期間

        public SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();

        public String WeeklyReportData { get; set; }
        public String WeeklyReportAnalysis { get; set; }

        //public WeeklyReportData m_WeeklyReportData = new WeeklyReportData();
    }
}
