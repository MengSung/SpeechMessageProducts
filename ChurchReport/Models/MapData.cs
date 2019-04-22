using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class MapData
    {
        //public String PresentRecordEntityId { get; set; }
        public String location { get; set; }
        public tooltip tooltip { get; set; }
    }
    public class tooltip
    {
        public String text { get; set; }
        public bool isShown { get; set; }
    }
}
