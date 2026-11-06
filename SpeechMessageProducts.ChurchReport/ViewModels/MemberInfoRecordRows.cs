/*
 * 檔案整體說明
 *
 * 目的與定位：定義會友彈窗中聚會記錄與上課記錄兩種列形狀，分別保留出席／上課正式ID及顯示欄位；記錄依所屬會友隔離，不能把不同會友的集合共用為快取值。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactPresentRecordRow、MemberInfoStorLessonRow。
 * 資料／選項欄位包含 ContactPresentRecordRow、PresentRecordId、FullName、SundayDate、Sunday、SmallGroup、PrayItem、
 * MemberInfoStorLessonRow、StorLessonsEntityId、DiscipleLessonsName、DiscipleLessonsDateTime、StageName、
 * CurrentComplete，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace ChurchReport.ViewModels
{
    public class ContactPresentRecordRow
    {
        public string PresentRecordId { get; set; }
        public string FullName { get; set; }
        public DateTime? SundayDate { get; set; }
        public bool Sunday { get; set; }
        public bool SmallGroup { get; set; }
        public string PrayItem { get; set; }
    }

    public class MemberInfoStorLessonRow
    {
        public string StorLessonsEntityId { get; set; }
        public string DiscipleLessonsName { get; set; }
        public DateTime DiscipleLessonsDateTime { get; set; }
        public string StageName { get; set; }
        public bool CurrentComplete { get; set; }
    }
}
