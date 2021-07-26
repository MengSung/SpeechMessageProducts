using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class UserIdList
    {
        public UserIdList()
        { }

        public List<String> userIds { get; set; }
    }
}
