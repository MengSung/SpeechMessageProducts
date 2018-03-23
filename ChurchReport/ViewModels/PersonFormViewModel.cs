using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.ViewModels
{
    public class PersonFormViewModel
    {
        public PersonFormViewModel()
        { }

        public int ID { get; set; }

        public String FirstName { get; set; }
        public String LastName { get; set; }
        public String Gender { get; set; }
        public String Phone { get; set; }
        public String HomePhone { get; set; }
        public String Position { get; set; }
        public String MerrageState { get; set; }
        public DateTime BirthDate { get; set; }
        public DateTime HireDate { get; set; } // 進入教會日期
        public String Notes { get; set; }
        public String Address { get; set; }
        public int ReadBibleNumber { get; set; }
        public String Status { get; set; } // 新人信仰狀態

        public String Introducer { get; set; } // 邀請人
        public String IntroducerPhone { get; set; } // 邀請人電話
        public String IntroducerRelation { get; set; } // 邀請人關係
        public String IntroducerGroup { get; set; } // 邀請人小組


        public object FormData { get; set; }
    }
}
