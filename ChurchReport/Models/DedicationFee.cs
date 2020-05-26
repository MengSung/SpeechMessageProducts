using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class DedicationFee
    {
        public DedicationFee()
        { }

        public string FullName { get; set; }                // 姓名
        public string MobilePhone { get; set; }             // 行動電話
        public DateTime DedicationDate { get; set; }        // 奉獻日期
        public DateTime PayDate { get; set; }               // 繳費日期
        public int Amount { get; set; }                     // 繳費金額
        public String PayWay { get; set; }                  // 付款方式
        public String Category { get; set; }                // 奉獻類別
        public String Others { get; set; }                  // 其他奉獻
    }
}
