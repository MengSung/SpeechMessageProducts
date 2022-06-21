using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class EquipmenSmallGroup
    {
        public string SmallGroupName { set; get; }  // 小組名稱

        public string LoginUserId { set; get; }     // 小組名單登入的使用者 Entity Id

        public string SmallGroupListEntityId { set; get; }    // 小組名單的 Entity Id

        public bool DirtyFlag { set; get; } = true;

        public List<EquipmentContact> EquipmentContactList { set; get; } // 小組名單的連絡人清單
    }
}
