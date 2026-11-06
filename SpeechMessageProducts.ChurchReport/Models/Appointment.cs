/*
 * 檔案整體說明
 *
 * 目的與定位：把CRM約會投影為行事曆元件使用的資料列，承載約會ID、類別、負責人、起訖時間、全天與週期規則；本檔是顯示／編輯資料契約，實際讀寫交由約會管理器及下載上傳服務。
 *
 * 主要功能與流程：
 * 本檔宣告 Appointment。
 * 資料／選項欄位包含 Appointment、AppointmentId、Text、AppointmentType、CategoryId、LeaveId、LocationId、OwnerId、StartDate、
 * EndDate、RecurrenceRule、AllDay、Description，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs、
 * SpeechMessageProducts.ChurchReport/Models/AppointmentsListManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace ChurchReport.Models
{
    public class Appointment
    {
        public String AppointmentId { get; set; }
        public string Text { get; set; }
        public string AppointmentType { get; set; }
        //public int[] CategoryId { get; set; }// 約會類別
        public int? CategoryId { get; set; }// 約會類別
        public int? LeaveId { get; set; }// 請假人資類別
        public int? LocationId { get; set; }// 場地類別
        public int[] OwnerId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string RecurrenceRule { get; set; }
        public bool AllDay { get; set; }
        public string Description { get; set; }
    }
}
