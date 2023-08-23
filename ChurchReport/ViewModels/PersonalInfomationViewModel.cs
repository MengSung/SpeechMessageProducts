using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.ViewModels
{
    public class PersonalInfomationViewModel
    {
        public PersonalInfomationViewModel()
        { }

        public int ID { get; set; }

        public String FirstName { get; set; }
        public String LastName { get; set; }
        public String FullName { get; set; }
        public String CustomerTypeCode { get; set; }//委身類型
        public String Gender { get; set; }
        public String Phone { get; set; } //行動電話
        public String HomePhone { get; set; } // 住家電話
        public String OfficePhone { get; set; } //公司電話
        public String Facebook { get; set; } //Facebook帳號
        public String Instagram { get; set; } //Instagram帳號
        public String Email { get; set; } //電子郵件
        public String Address { get; set; } // 地址
        public String LastSixDigit { get; set; } // 銀行帳戶後六碼
        //public bool NtbtOrNot { get; set; } // 是否上傳國稅局
        public String NtbtOrNot { get; set; } // 是否上傳國稅局
        public String PersonalId { get; set; } // 身份證字號
        public String Position { get; set; }
        public List<String> GroupArray { get; set; }
        public String GroupName { get; set; }
        public String MerrageState { get; set; }
        public DateTime BirthDate { get; set; }
        public DateTime HireDate { get; set; } // 進入教會日期
        public String Notes { get; set; }
        public int ReadBibleNumber { get; set; }
        public String Status { get; set; } // 新人信仰狀態

        public String Introducer { get; set; } // 邀請人
        public String IntroducerPhone { get; set; } // 邀請人電話
        public String IntroducerRelation { get; set; } // 邀請人關係
        public String IntroducerGroup { get; set; } // 邀請人小組

        public String Industry { get; set; } // 職業及專長
        public String EquipmentStatus { get; set; } // 裝備狀態
        public String SpiritualIdentity { get; set; } // 受洗狀態

        public String PresentRecordId { get; set; } // 建立新人時也會新增建立的靈修出席紀錄單的ID

        public object FormData { get; set; }
    }
}
