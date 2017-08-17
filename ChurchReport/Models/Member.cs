using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class Member
    {
        public Member()
        { }

        public int Id { get; set; }
        public string Group { get; set; }
        public string FullName { get; set; }
        public string Status { get; set; } // 委身類型
        public string SmallGroupName { get; set; }
        public string SectionName { get; set; }
        public string PrayItem { get; set; }
        public bool Sunday { get; set; }
        public bool SmallGroup { get; set; }
        public int StateID1 { get; set; }
        public int Number1 { get; set; }
        public int StateID2 { get; set; }
        public int Number2 { get; set; }

        public string Picture { get; set; }
        public string Shepherd { get; set; }
    }
}
