using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class QpayModel
    {
        public QpayModel()
        { }

        public String FullName { get; set; }                            //姓名
        public String Mobile { get; set; }                              //手機
        public String DedicationNumber { get; set; }                    //奉獻編號
        public DateTime DedicationDate { get; set; } = DateTime.Now;    //奉獻日期
        public String BankName { get; set; }                            //銀行名稱
        public String SerialNumber { get; set; }                        //奉獻號碼
        public int Amount { get; set; }                                 //奉獻金額
        public String Category { get; set; }                            //奉獻類別
        public String PayWay { get; set; }                              //付款方式
        public String Others { get; set; }                              //其他奉獻
        public List<String> OtherCategoryArray { get; set; }
        public List<CreditCard> CreditCardList { get; set; } // 
        public List<DedicationFee> DedicationFeeList { get; set; } // 
        public int TotalAmount { get; set; }                            //奉獻總金額

        public DateTime QueryStartDate { get; set; } = DateTime.Now;    //奉獻查詢開始日期
        public DateTime QueryEndDate { get; set; } = DateTime.Now;      //奉獻查詢結束日期

    }
}
