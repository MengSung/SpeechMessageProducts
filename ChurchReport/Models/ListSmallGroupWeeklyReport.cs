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

        public bool SmallGroupDisplayFlag { get; set; } // 小組牧養的表格是否顯示的旗標
        public bool NewPersonFollowUpDisplayFlag { get; set; } // 新人跟進關懷的表格是否顯示的旗標

        public SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();

        public String WeeklyReportData { get; set; }
        public String WeeklyReportAnalysis { get; set; }

        // 圖表資料
        public ChartDataList m_WeeklyReportChart { get; set; }

        public bool ModifyFlag { get; set; }

    }
}
