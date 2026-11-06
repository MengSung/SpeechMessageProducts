/*
 * 檔案整體說明
 *
 * 目的與定位：承載裝備管理中單一學員的聯絡人ID、小組範圍、裝備狀態及上課記錄清單，保留學員與BEST欄位的個別異動旗標供批次上傳判斷。
 *
 * 主要功能與流程：
 * 本檔宣告 EquipmentContact。
 * 資料／選項欄位包含 EquipmentContact、SmallGroupName、SmallGroupListEntityId、EquipmentContactId、ContactFullName、
 * EquipmentStatus、EquipmentContactModifiedFlag、BestRecordModifiedFlag、ModifiedFlag、StorLessonsList，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs、
 * SpeechMessageProducts.ChurchReport/Models/EquipmenSmallGroup.cs、
 * SpeechMessageProducts.ChurchReport/Models/EquipmentDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentContactView.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentView.cshtml 追查目前工作樹中的合作／使用位置。
 */
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
