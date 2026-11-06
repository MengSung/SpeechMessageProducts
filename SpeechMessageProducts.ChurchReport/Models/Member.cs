/*
 * 檔案整體說明
 *
 * 目的與定位：保存小組週報及整合名單使用的單一會友編輯資料，將聯絡人身分、出席／關懷狀態與異動欄位帶往既有上傳流程；它不是跨使用者共用的CRM實體快取。
 *
 * 主要功能與流程：
 * 本檔宣告 Member。
 * - 建立 Member 的獨立副本。 此建構式只複製成員本身的值，不保留指向原始週報或其他可變物件圖的參考； 背景上傳快照因此可在自己的生命週期內清理欄位，而不會改寫前景 Session 所持有的成員。
 * 字串與值型別可直接複製，ParentListSmallGroupWeeklyReport 則刻意留空，避免把父物件圖重新連回共用狀態。
 * 資料／選項欄位包含 Member、PresentRecordId、ContactId、Id、Group、FullName、Status、Phone、HomePhone、Address、BirthDate、Industry、
 * EquipmentStatus、SpiritualIdentity、BaptizedSituation、Description，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class Member
    {
        public Member()
        { }

        /// <summary>
        /// 建立 Member 的獨立副本。
        ///
        /// 此建構式只複製成員本身的值，不保留指向原始週報或其他可變物件圖的參考；
        /// 背景上傳快照因此可在自己的生命週期內清理欄位，而不會改寫前景 Session
        /// 所持有的成員。字串與值型別可直接複製，ParentListSmallGroupWeeklyReport
        /// 則刻意留空，避免把父物件圖重新連回共用狀態。
        /// </summary>
        /// <param name="source">要複製的來源成員；不可為 null。</param>
        /// <exception cref="ArgumentNullException">來源成員為 null 時擲出。</exception>
        public Member(Member source)
        {
            ArgumentNullException.ThrowIfNull(source);

            PresentRecordId = source.PresentRecordId;
            ContactId = source.ContactId;
            Id = source.Id;
            Group = source.Group;
            FullName = source.FullName;
            Status = source.Status;
            Phone = source.Phone;
            HomePhone = source.HomePhone;
            Address = source.Address;
            BirthDate = source.BirthDate;
            Industry = source.Industry;
            EquipmentStatus = source.EquipmentStatus;
            SpiritualIdentity = source.SpiritualIdentity;
            BaptizedSituation = source.BaptizedSituation;
            Description = source.Description;
            SmallGroupName = source.SmallGroupName;
            SectionName = source.SectionName;
            PrayItem = source.PrayItem;
            Visit = source.Visit;
            Sunday = source.Sunday;
            SmallGroup = source.SmallGroup;
            PrayerMeeting = source.PrayerMeeting;
            Child = source.Child;
            BigDisciple = source.BigDisciple;
            LeadershipSmallLecture = source.LeadershipSmallLecture;
            LeadersGather = source.LeadersGather;
            Decision = source.Decision;
            StateID1 = source.StateID1;
            Number1 = source.Number1;
            StateID2 = source.StateID2;
            Number2 = source.Number2;
            FollowUpWeek = source.FollowUpWeek;
            FollowUpOption = source.FollowUpOption;
            FollowUp = source.FollowUp;
            FollowUpResult = source.FollowUpResult;
            FollowUpNextStep = source.FollowUpNextStep;
            FollowUpNote = source.FollowUpNote;
            NewComerNote = source.NewComerNote;
            AssignedGroup = source.AssignedGroup;
            SpiritualWork = source.SpiritualWork;
            MorningPray = source.MorningPray;
            GeneralCare = source.GeneralCare;
            Picture = source.Picture;
            Shepherd = source.Shepherd;
            BestLeader = source.BestLeader;
            BestIntroducer = source.BestIntroducer;
            BestRelationship = source.BestRelationship;
            ModifyFlag = source.ModifyFlag;
        }

        ListSmallGroupWeeklyReport ParentListSmallGroupWeeklyReport { get; set; }

        // CRM 實體 ID
        public String PresentRecordId { get; set; }  // Present Record ID (出席記錄 ID)
        public String ContactId { get; set; }        // Contact ID (聯絡人 ID) - 用於查詢課程記錄等功能

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

        // 洗禮狀態(長老教會專用)
        public string BaptizedSituation
        {
            get;
            set;
        }

        // 個人附註
        public string Description
        {
            get;
            set;
        }
        #endregion

        public string SmallGroupName { get; set; }
        public string SectionName { get; set; }
        public string PrayItem { get; set; }
        public string Visit { get; set; } // 探訪欄位
        public bool Sunday { get; set; }                    // 主日出席
        public bool SmallGroup { get; set; }                // 小組出席
        public bool PrayerMeeting { get; set; }             // 禱告會
        public bool Child { get; set; }                     // 門徒禱告訓練班
        public bool BigDisciple { get; set; }               // 門徒大聚
        public bool LeadershipSmallLecture { get; set; }    // 小組長小講堂
        public bool LeadersGather { get; set; }             // 小組長大聚
        public bool Decision { get; set; }                  // 決志

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
        // 換小組要用到的小組
        public string AssignedGroup
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
