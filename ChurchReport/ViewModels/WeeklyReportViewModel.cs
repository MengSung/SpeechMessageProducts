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
        ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        public String WeeklyReportData { get; set; }
        public String WeeklyReportAnalysis { get; set; }

        //static public WeeklyReportData m_WeeklyReportData = new WeeklyReportData();

    }
}
