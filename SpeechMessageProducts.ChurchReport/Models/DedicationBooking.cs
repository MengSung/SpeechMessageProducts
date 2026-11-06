/*
 * 檔案整體說明
 *
 * 目的與定位：將認獻單的正式ID、類別、每期金額、總期數、已付期數與起訖日期提供給產品流程，區分承諾金額與已收費累計；是否扣款及更新狀態由付款服務決定。
 *
 * 主要功能與流程：
 * 本檔宣告 DedicationBooking。
 * 資料／選項欄位包含 DedicationBooking、EntityId、DedicationCategory、AmountPerStage、DedicationBookingStatus、TotalStages、
 * PaidPeriod、DedicationAmount、RollupPaidFee、StartDate、EndDate，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ToolUtility.Tests/DataOperations/ColumnContractTests.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationBookingService.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationPaymentModelAssembler.cs 追查目前工作樹中的合作／使用位置。
 */
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
