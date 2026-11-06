/*
 * 檔案整體說明
 *
 * 目的與定位：表示學員一筆上課紀錄，區分小組ID、聯絡人ID與上課紀錄ID，保存課程、階段、日期、結業及修改狀態；寫回必須使用正式記錄ID。
 *
 * 主要功能與流程：
 * 本檔宣告 EquipmentStorLessons。
 * 資料／選項欄位包含 EquipmentStorLessons、SmallGroupName、SmallGroupListEntityId、EquipmentContactId、StorLessonsEntityId、
 * StorLessonsName、DiscipleLessonsName、StageName、DiscipleLessonsDateTime、CurrentComplete、ModifiedFlag，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs、
 * SpeechMessageProducts.ChurchReport/Models/EquipmentContact.cs、
 * SpeechMessageProducts.ChurchReport/Models/EquipmentDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentStorLessonsView.cshtml、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs 追查目前工作樹中的合作／使用位置。
 */
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
