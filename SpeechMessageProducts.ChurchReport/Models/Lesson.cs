/*
 * 檔案整體說明
 *
 * 目的與定位：將可選課程的正式ID、報名期間、上課期間及報名人數投影為課程選單資料；顯示課名與日期不能代替提交時的課程ID及伺服器驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 Lesson。
 * 資料／選項欄位包含 Lesson、DiscipleLessonsName、DiscipleLessonsId、EnrollStartDate、EnrollEndDate、LessonStartDate、
 * LessonEndDate、EnrolledNumber，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs、
 * SpeechMessageProducts.ChurchReport/Models/FeeList.cs、
 * SpeechMessageProducts.ChurchReport/Views/FeeManagement/LessonList.cshtml、
 * SpeechMessageProducts.ChurchReport/Views/Home/PresentFeeListView.cshtml、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class Lesson
    {
        public Lesson()
        { }

        public string DiscipleLessonsName { get; set; }       // 教會課程名稱
        public string DiscipleLessonsId { get; set; }       // 教會課程 Id
        public DateTime EnrollStartDate { get; set; }               // 報名開始日期
        public DateTime EnrollEndDate { get; set; }               // 報名結束日期
        public DateTime LessonStartDate { get; set; }               // 課程開始日期
        public DateTime LessonEndDate { get; set; }               // 課程結束日期
        public int EnrolledNumber { get; set; }                     // 報名人數

    }
}
