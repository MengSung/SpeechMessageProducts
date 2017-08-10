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
        public String SmallGroupLeaderFullName { get; set; }
        public DateTime SundayPrayers { get; set; }

        public List<Member> members;

        public List<Member> Members { get => members; set => members = value; }
    }
}
