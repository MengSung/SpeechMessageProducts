using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class DedicationInfoModel
    {
        public DedicationInfoModel()
        { }

        public DateTime DedicationDate { get; set; }    //奉獻日期
        public String BankName { get; set; }            //銀行名稱
        public String SerialNumber { get; set; }        //奉獻號碼
        public int Amount { get; set; }                 //奉獻金額
        public String Category { get; set; }            //奉獻類別
        public String Others { get; set; }              //其他奉獻

    }
}
