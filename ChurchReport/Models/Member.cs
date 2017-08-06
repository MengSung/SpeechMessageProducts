using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class Member
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string SmallGroupName { get; set; }
        public string SectionName { get; set; }
        public string PrayItem { get; set; }
        public bool Sunday { get; set; }
        public bool SmallGroup { get; set; }
        public int StateID { get; set; }
        public int Number { get; set; }

        public string Picture { get; set; }
    }
}
