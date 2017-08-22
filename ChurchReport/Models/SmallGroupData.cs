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
        {

        }
        public String SmallGroupLeaderContactId { get; set; }
        public String SmallGroupLeaderFullName { get; set; }
        public DateTime SundayPrayers { get; set; }

        public String DataStatus { get; set; }

        //public List<Member> m_Members;

        //public List<Member> Members { get => m_Members; set => m_Members = value; }
        public List<Member> Members { get ; set ; }
    }
}
