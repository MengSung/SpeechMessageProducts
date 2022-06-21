using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    // 裝備的上課紀錄單
    public class EquipmentStorLessons
    {
        public string SmallGroupName { set; get; }              // 小組名稱
        public string SmallGroupListEntityId { set; get; }      // 在 CRM 系統中小組名單的 EntityId
        public string EquipmentContactId { set; get; }          // 連絡人的在回報陣列中的Id ，也會是 小組在 CRM 系統中的 EntityId
        public string StorLessonsEntityId { set; get; }         // 裝備的上課紀錄單在 CRM 系統中的 EntityId

        public string StorLessonsName { get; set; }             // 裝備的上課紀錄單名稱
        public string DiscipleLessonsName { get; set; }         // 課程名稱
        public string StageName { get; set; }                   // 階段名稱
        public DateTime DiscipleLessonsDateTime { get; set; }   // 課程日期
        public bool CurrentComplete { get; set; }               // 是否結業

        public bool ModifiedFlag { set; get; } = false;         // 是否有被修改過

    }
}
