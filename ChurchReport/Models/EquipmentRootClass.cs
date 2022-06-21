using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class EquipmentRootClass
    {
        // 小組名單登入的使用者 Entity Id
        public string LoginUserId { set; get; } 

        // 一個人會有一個以上的小組
        public List<EquipmenSmallGroup> EquipmenSmallGroupList { set; get; } // 小組清單
    }
}
