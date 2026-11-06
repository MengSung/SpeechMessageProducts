/*
 * 檔案整體說明
 *
 * 目的與定位：表示裝備管理樹中的小組節點，帶入小組名單ID、登入識別、異動旗標與裝備學員清單；不同ID的小組即使同名仍是不同節點。
 *
 * 主要功能與流程：
 * 本檔宣告 EquipmenSmallGroup。
 * 資料／選項欄位包含 EquipmenSmallGroup、SmallGroupName、LoginUserId、SmallGroupListEntityId、DirtyFlag、EquipmentContactList，
 * 名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs、
 * SpeechMessageProducts.ChurchReport/Models/EquipmentDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/EquipmentRootClass.cs、
 * SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentView.cshtml、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
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
