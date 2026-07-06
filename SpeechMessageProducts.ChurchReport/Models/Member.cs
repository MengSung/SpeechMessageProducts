// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/Member.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class Member
// 主要成員：PresentRecordId、ContactId、Id、Group、FullName、Status、Phone、HomePhone、Address、BirthDate
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Text、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
