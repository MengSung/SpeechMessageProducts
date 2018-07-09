using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

using ChurchReport.Models;

namespace ChurchReport.ViewModels
{
    public class WeeklyReportViewModel
    {
        public String WeeklyReportData { get; set; }
        public String WeeklyReportAnalysis { get; set; }
        public bool DisplayFlag { get; set; }

        //static public WeeklyReportData m_WeeklyReportData = new WeeklyReportData();
    }
}
