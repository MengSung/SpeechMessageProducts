using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class DedicationModel
    {
        public DedicationModel()
        { }

        public int Amount { get; set; }
        public String Category { get; set; }
        
        // ✅ 新增：動態奉獻類別清單（從 Dynamics 365 OptionSet 取得）
        public List<String> DedicationCategoryList { get; set; } = new List<String>();
    }
}
