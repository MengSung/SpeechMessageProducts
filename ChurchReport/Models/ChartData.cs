using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class ChartData
    {
        public String WeeklyReportEntityId { get; set; }
        public String SundayDate { get; set; }
        public int SundayNumber { get; set; }
        public int SmallNumber { get; set; }
    }
}
