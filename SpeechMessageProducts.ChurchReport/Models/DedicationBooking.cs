// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/DedicationBooking.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class DedicationBooking
// 主要成員：EntityId、DedicationCategory、AmountPerStage、DedicationBookingStatus、TotalStages、PaidPeriod、DedicationAmount、RollupPaidFee、StartDate、EndDate
// 引用命名空間：System、System.Collections.Generic、System.ComponentModel.DataAnnotations、System.Linq、System.Text、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class DedicationBooking
    {
        public DedicationBooking()
        { }

        public string EntityId { get; set; }                    // 紀錄的ID
        public string DedicationCategory { get; set; }          // 奉獻類別
        public string AmountPerStage { get; set; }              // 每期金額
        public string DedicationBookingStatus { get; set; }     // 奉獻狀態
        public string TotalStages { get; set; }                 // 總期數
        public string PaidPeriod { get; set; }                  // 目前期數
        public string DedicationAmount { get; set; }            // 應付總金額
        public string RollupPaidFee { get; set; }               // 已付金額
        public string StartDate { get; set; }                   // 認獻開始日期
        public string EndDate { get; set; }                     // 認獻結束日期
    }
}
