using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    // 連絡人
    public class EquipmentContact
    {
        public string SmallGroupName { set; get; }              // 小組名稱
        public string SmallGroupListEntityId { set; get; }      // 在 CRM 系統中小組名單的 EntityId
        public string EquipmentContactId { set; get; }          // 連絡人的在回報陣列中的Id ，也會是 小組在 CRM 系統中的 EntityId

        public string ContactFullName { set; get; }             // 連絡人姓名
        public string EquipmentStatus { set; get; }             // 裝備狀態

        public bool EquipmentContactModifiedFlag { get; set; }
        public bool BestRecordModifiedFlag { get; set; }

        public bool ModifiedFlag { set; get; } = false;         // 是否有被修改過
        public List<EquipmentStorLessons> StorLessonsList { set; get; }  // 上課紀錄清單
    }
}
