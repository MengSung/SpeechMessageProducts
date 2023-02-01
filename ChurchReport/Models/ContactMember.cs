using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class ContactMember
    {
        // 名單管理要用到的成員
        public ContactMember()
        { }

        public string FullName { get; set; }
        public String ContactId { get; set; }
        public string SmallGroupName { get; set; }
        public string SmallGroupId { get; set; }
        public string Status { get; set; } // 委身類型
        public string RaceLeaderSmallGroup { get; set; } // 本區小組
        public string ChurchSmallGroup { get; set; } // 全教會小組

        #region 個人基本資料
        public string MobilePhone
        {
            get;
            set;
        }

        public string HomePhone
        {
            get;
            set;
        }

        public string Address
        {
            get;
            set;
        }

        public DateTime BirthDate
        {
            get;
            set;
        }
        public string Industry
        {
            get;
            set;
        }

        // 裝備狀態
        public string EquipmentStatus
        {
            get;
            set;
        }

        // 受洗狀態
        public string SpiritualIdentity
        {
            get;
            set;
        }

        #endregion

        public bool ModifyFlag { get; set; }

    }
}
