
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Models
{
    public partial class ShepherdMethodData
    {
        public static List<ShepherdMethod> ShepherdMethodList = new List<ShepherdMethod> {
            new ShepherdMethod {
                ID = 1,
                Name = "打電話"
            },
            new ShepherdMethod {
                ID = 2,
                Name = "一起吃飯"
            },
            new ShepherdMethod {
                ID = 3,
                Name = "陪讀聖經"
            },
            new ShepherdMethod {
                ID = 4,
                Name = "Line聯絡"
            },
            new ShepherdMethod {
                ID = 5,
                Name = "親自拜訪"
            },
        };
    }
}