using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class SmallGroupData
    {
        public SmallGroupData()
        { }
        public String LoginType { get; set; }
        public String SmallGroupLeaderContactId { get; set; }
        public String SmallGroupLeaderFullName { get; set; }
        public DateTime SundayPrayers { get; set; }
        public String DataStatus { get; set; }

        public List<Member> Members { get ; set ; }
    }
}
