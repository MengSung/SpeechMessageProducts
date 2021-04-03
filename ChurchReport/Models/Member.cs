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

        ListSmallGroupWeeklyReport ParentListSmallGroupWeeklyReport { get; set; }

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

        public string SmallGroupName { get; set; }
        public string SectionName { get; set; }
        public string PrayItem { get; set; }
        public bool Sunday { get; set; }                    // 主日出席
        public bool SmallGroup { get; set; }                // 小組出席
        public bool PrayerMeeting { get; set; }             // 禱告會
        public bool Child { get; set; }                     // 門徒禱告訓練班
        public bool BigDisciple { get; set; }               // 門徒大聚
        public bool LeadershipSmallLecture { get; set; }    // 領袖小講堂
        public bool LeadersGather { get; set; }             // 領袖大聚
        public bool Decision { get; set; } // 決志

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
        public int SpiritualWork { get; set; } // 讀經次數
        public int MorningPray { get; set; }   // 晨禱(家庭祭壇)
        public int GeneralCare { get; set; }   // 晚禱(禱告會次數)
        #endregion

        public string Picture { get; set; }
        public string Shepherd { get; set; }
        public string BestLeader { get; set; } // 屬靈認領者
        public string BestIntroducer { get; set; } // 介紹人
        public string BestRelationship { get; set; } // 與介紹人關係
        public bool ModifyFlag { get; set; }

    }
}
