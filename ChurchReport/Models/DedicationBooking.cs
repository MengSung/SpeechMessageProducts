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
