using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models.CrmTransmitModule
{

    public class GroupWeeklyReportGuid
    {
        public GroupWeeklyReportGuid()
        {
        }
        Guid m_WeeklyReportGuid = Guid.NewGuid();
        public Guid WeeklyReportGuid
        {
            get { return m_WeeklyReportGuid; }
            set { m_WeeklyReportGuid = value; }
        }

        String m_GroupName; //小組名稱
        public String GroupName
        {
            get { return m_GroupName; }
            set { m_GroupName = value; }
        }

        Double m_SundayPresentRate; //主日出席率
        public Double SundayPresentRate
        {
            get { return m_SundayPresentRate; }
            set { m_SundayPresentRate = value; }
        }

        Double m_SmallGroupRate; //小組出席率
        public Double SmallGroupRate
        {
            get { return m_SmallGroupRate; }
            set { m_SmallGroupRate = value; }
        }
    }

    public class AccountPasswordData
    {
        public AccountPasswordData()
        {
            Account = "Tester";
            Password = "Password";
        }

        public string Account { get; set; }

        public string Password { get; set; }
    }

    public class MemberInfomation
    {
        public MemberInfomation()
        {
        }
        string m_Group = "";
        public string Group
        {
            get { return m_Group; }
            set { m_Group = value; }
        }

        string m_Name = "";
        public string Name
        {
            get { return m_Name; }
            set { m_Name = value; }
        }

        // 委身類型
        string m_Identity = "";
        public string Identity
        {
            get { return m_Identity; }
            set { m_Identity = value; }
        }

        string m_Status = "";
        public string Status
        {
            get { return m_Status; }
            set { m_Status = value; }
        }

        string m_Phone = "";
        public string Phone
        {
            get { return m_Phone; }
            set { m_Phone = value; }
        }

        string m_HomePhone = "";
        public string HomePhone
        {
            get { return m_HomePhone; }
            set { m_HomePhone = value; }
        }

        string m_Address = "";
        public string Address
        {
            get { return m_Address; }
            set { m_Address = value; }
        }

        string m_Note = "";
        public string Note
        {
            get { return m_Note; }
            set { m_Note = value; }
        }

        string m_Date = "";
        public string Date
        {
            get { return m_Date; }
            set { m_Date = value; }
        }

        int m_Number = 0;
        public int Number
        {
            get { return m_Number; }
            set { m_Number = value; }
        }

        bool m_SundayPresent = false;
        public bool SundayPresent
        {
            get { return m_SundayPresent; }
            set { m_SundayPresent = value; }
        }

        bool m_SmallGroupPresent = false;
        public bool SmallGroupPresent
        {
            get { return m_SmallGroupPresent; }
            set { m_SmallGroupPresent = value; }
        }

        int m_PrayNumber = 0;
        public int PrayNumber
        {
            get { return m_PrayNumber; }
            set { m_PrayNumber = value; }
        }

        int m_SpiritNumber = 0;
        public int SpiritNumber
        {
            get { return m_SpiritNumber; }
            set { m_SpiritNumber = value; }
        }

        int m_FamilyNumber = 0;
        public int FamilyNumber
        {
            get { return m_FamilyNumber; }
            set { m_FamilyNumber = value; }
        }

        int m_WorkAndCampusNumber = 0;
        public int WorkAndCampusNumber
        {
            get { return m_WorkAndCampusNumber; }
            set { m_WorkAndCampusNumber = value; }
        }

        // 牧養狀態
        string m_ShepherdStatus = "";
        public string ShepherdStatus
        {
            get { return m_ShepherdStatus; }
            set { m_ShepherdStatus = value; }
        }

        // 一對一牧養材料選項
        string m_OneOnOne = "";
        public string OneOnOne
        {
            get { return m_OneOnOne; }
            set { m_OneOnOne = value; }
        }

        // 培訓系統選項
        string m_Training = "";
        public string Training
        {
            get { return m_Training; }
            set { m_Training = value; }
        }

        // 裝備課程選項
        string m_Incubate = "";
        public string Incubate
        {
            get { return m_Incubate; }
            set { m_Incubate = value; }
        }

        // 跟進週次
        string m_FollowUpWeek = "";
        public string FollowUpWeek
        {
            get { return m_FollowUpWeek; }
            set { m_FollowUpWeek = value; }
        }

        // 跟進結果
        string m_FollowUpResult = "";
        public string FollowUpResult
        {
            get { return m_FollowUpResult; }
            set { m_FollowUpResult = value; }
        }

        // 跟進方式
        string m_FollowUp = "";
        public string FollowUp
        {
            get { return m_FollowUp; }
            set { m_FollowUp = value; }
        }

        // 跟進下一步驟
        string m_FollowUpNextStep = "";
        public string FollowUpNextStep
        {
            get { return m_FollowUpNextStep; }
            set { m_FollowUpNextStep = value; }
        }

        // 跟進附註
        string m_FollowUpNote = "";
        public string FollowUpNote
        {
            get { return m_FollowUpNote; }
            set { m_FollowUpNote = value; }
        }

        // 新人跟進歷程
        string m_NewComerNote = "";
        public string NewComerNote
        {
            get { return m_NewComerNote; }
            set { m_NewComerNote = value; }
        }
    }

    public class MemberInfomationPackage
    {
        #region 除錯用參數
        private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
        #endregion

        public MemberInfomationPackage()
        {
        }

        Guid m_WeeklyReportGuid = Guid.NewGuid();
        public Guid WeeklyReportGuid
        {
            get { return m_WeeklyReportGuid; }
            set { m_WeeklyReportGuid = value; }
        }

        List<GroupWeeklyReportGuid> m_GroupWeeklyReportGuidList;
        public List<GroupWeeklyReportGuid> GroupWeeklyReportGuidList
        {
            get { return m_GroupWeeklyReportGuidList; }
            set { m_GroupWeeklyReportGuidList = value; }
        }

        List<MemberInfomation> m_ListMemberInfomation;
        public List<MemberInfomation> ListMemberInfomation
        {
            get { return m_ListMemberInfomation; }
            set { m_ListMemberInfomation = value; }
        }

    }

    public class WeeklyReport
    {
        public WeeklyReport()
        {
        }

        //慕道友數
        int m_ReligiousInvestigator = 0;
        public int ReligiousInvestigator
        {
            get { return m_ReligiousInvestigator; }
            set { m_ReligiousInvestigator = value; }
        }

        //受洗數
        int m_Baptized = 0;
        public int Baptized
        {
            get { return m_Baptized; }
            set { m_Baptized = value; }
        }

        //跟進次數
        int m_FollowNumber = 0;
        public int FollowNumber
        {
            get { return m_FollowNumber; }
            set { m_FollowNumber = value; }
        }

        // 推動方式
        string m_PushMethod = "";
        public string PushMethod
        {
            get { return m_PushMethod; }
            set { m_PushMethod = value; }
        }

        // 進行方式
        string m_ProgressMethod = "";
        public string ProgressMethod
        {
            get { return m_ProgressMethod; }
            set { m_ProgressMethod = value; }
        }

        // 一對一材料及人數結果
        string m_OneOnOne = "";
        public string OneOnOne
        {
            get { return m_OneOnOne; }
            set { m_OneOnOne = value; }
        }

        // 小組日誌
        string m_WeeklyReportContent = "";
        public string WeeklyReportContent
        {
            get { return m_WeeklyReportContent; }
            set { m_WeeklyReportContent = value; }
        }

        // 出席紀錄
        string m_PresentContent = "";
        public string PresentContent
        {
            get { return m_PresentContent; }
            set { m_PresentContent = value; }
        }
    }


}
