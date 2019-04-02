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
        public SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();
        public WeeklyReportData m_WeeklyReportData = new WeeklyReportData();
    }
}
