using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models {
    public class Member
    {
        public Member()
        { }

        public String PresentRecordId { get; set; }
        public int Id { get; set; }
        public string Group { get; set; }
        public string FullName { get; set; }
        public string Status { get; set; } // 委身類型
        #region 個人基本資料
        public string Phone
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
        //public DateTime BirthDate
        //{
        //    get;
        //    set;
        //}
        public string Industry
        {
            get;
            set;
        }
        #endregion

        public string SmallGroupName { get; set; }
        public string SectionName { get; set; }
        public string PrayItem { get; set; }
        public bool Sunday { get; set; }
        public bool SmallGroup { get; set; }
        public int StateID1 { get; set; }
        public int Number1 { get; set; }
        public int StateID2 { get; set; }
        public int Number2 { get; set; }

        #region 新人跟進關懷
        // 跟進週次
        public string FollowUpWeek
        {
            get;
            set;
        }

        // 跟進方式選項
        public string FollowUpOption
        {
            get;
            set;
        }

        // 跟進方式
        public string FollowUp
        {
            get;
            set;
        }

        // 跟進結果
        public string FollowUpResult
        {
            get;
            set;
        }

        // 跟進下一步驟
        public string FollowUpNextStep
        {
            get;
            set;
        }

        // 跟進附註
        public string FollowUpNote
        {
            get;
            set;
        }

        // 新人跟進歷程
        public string NewComerNote
        {
            get;
            set;
        }
        #endregion

        #region 靈修、晨、晚禱
        public int SpiritualWork { get; set; } // 靈修次數
        public int MorningPray { get; set; }   // 晨禱(家庭祭壇)
        public int GeneralCare { get; set; }   // 晚禱(禱告會次數)
        #endregion

        public string Picture { get; set; }
        public string Shepherd { get; set; }
    }
}
