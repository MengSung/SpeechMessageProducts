using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.ViewModels
{
    public class PersonalReportViewModel
    {
        public PersonalReportViewModel()
        { }

        public int ID { get; set; }

        public String GroupName { get; set; }
        public String FullName { get; set; }
        public bool SundayPresent { get; set; }
        public bool SmallGroupPresent { get; set; }
        public int SpiritualWork { get; set; }
        public int MorningPray { get; set; }
        public int GeneralCare { get; set; }
        public String PrayItem { get; set; }
    }
}
