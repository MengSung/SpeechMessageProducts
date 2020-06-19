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
        public String NationId { get; set; }                            //身分證字號
        public DateTime DedicationDate { get; set; } = DateTime.Now;    //奉獻日期
        public String BankName { get; set; }                            //銀行名稱
        public String SerialNumber { get; set; }                        //奉獻號碼
        public int Amount { get; set; }                                 //奉獻金額
        public String Category { get; set; }                            //奉獻類別
        public String PayWay { get; set; }                              //付款方式
        public String Others { get; set; }                              //其他奉獻
        public String DedicateLocation { get; set; }                    //奉獻分堂
        public String Explain { get; set; }                             //備註
        public DateTime RecieptDate { get; set; } = DateTime.Now;       //收據日期
        public String ClickType { get; set; }                           //查詢或是上傳
        public String SelectedCreditCard { get; set; }                  //選取的信用卡
        public List<String> OtherCategoryArray { get; set; }
        public List<CreditCard> CreditCardList { get; set; }
        public List<DedicationFee> DedicationFeeList { get; set; }
        public int TotalAmount { get; set; }                            //奉獻總金額

        public DateTime QueryStartDate { get; set; } = DateTime.Now;    //奉獻查詢開始日期
        public DateTime QueryEndDate { get; set; } = DateTime.Now;      //奉獻查詢結束日期

        public bool IsAOfficeWorker { get; set; } = false;              //是否符合輸入奉獻的行政人員

        public List<SameNameElement> SameNameList { get; set; } = new List<SameNameElement>();

    }
}
