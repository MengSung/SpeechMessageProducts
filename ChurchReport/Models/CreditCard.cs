using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class CreditCard
    {
        public CreditCard()
        { }

        public string CCToken { get; set; }             // CCToken
        public string LeftCardNumber { get; set; }    // 信用卡卡號
        public string RightCardNumber { get; set; }    // 信用卡卡號
        public string CreditCardNumber { get; set; }    // 信用卡卡號
        public string ExpireDate { get; set; }          // 過期日
    }
}
