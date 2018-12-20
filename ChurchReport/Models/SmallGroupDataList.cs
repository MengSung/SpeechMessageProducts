using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;


// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ChurchReport.WebServiceConnector;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.ViewModels;

namespace ChurchReport.Models
{
    public class SmallGroupDataList
    {
        public String m_FullName = "";
        public String m_Account  = "";
        public String m_Password = "";
        //public DateTime m_SelectDate = new DateTime(2000, 1, 1);// 初始值 2000 表示還沒選
        public DateTime m_SelectDate = DateTime.Now;// 初始值 2000 表示還沒選
        public DateTime m_SundayDate;
        private bool m_FirstLoginFlag;

        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //static ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        // 小組長點名
        public SmallGroupData m_SmallGroupData = new SmallGroupData();

        // 新人跟進關懷
        public SmallGroupData m_NewPersonFollowUpData = new SmallGroupData();

        // 全部的名單，更新基本資料要用的
        public SmallGroupData m_AllMemeberData = new SmallGroupData();

        public MemberInfomationPackage m_MemberInfomationPackage;

        public AssignSmallGroupList m_AssignSmallGroupList = new AssignSmallGroupList();

        public void SetupContactIdString(String ContactIdString)
        {
            m_SmallGroupData.SmallGroupLeaderContactId = ContactIdString;
        }
        public void SetupSmallGroupData(String FullName, String Account, String Password, DateTime aSelectDate, bool DisplayDateFlag)
        {
            m_FullName = FullName;
            m_Account = Account;
            m_Password = Password;
            // 是否是首次登入，是的話小組日期就是從後台小組日期決定，否則就是登入想要改小組日期
            m_FirstLoginFlag = DisplayDateFlag; 
            DownloadData aDownloader = new DownloadData();
            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = Account,
                Password = Password
            };

            // 從雲端後台下載下來小組點名資料
            m_SundayDate = aSelectDate.AddDays(-(int)aSelectDate.DayOfWeek);
            // 取得所有需要的資料
            m_MemberInfomationPackage = aDownloader.GetMemberDataPackage(m_SundayDate, aAccountPasswordData);


            m_SmallGroupData.LoginType = m_MemberInfomationPackage.m_LoginType;

            m_SmallGroupData.SmallGroupLeaderFullName = FullName;
            m_SelectDate = aSelectDate;
            m_SmallGroupData.SundayPrayers = aSelectDate;
            //m_SmallGroupData.SundayPrayers = m_SundayDate;

            // 提醒小組長回報的期間
            m_SmallGroupData.SundayPeriod = FullName + "選擇的小組日期對應到主日期間是: " + m_SundayDate.ToLocalTime().ToShortDateString() + " ~ " + m_SundayDate.AddDays(6).ToLocalTime().ToShortDateString();

            SetSundayPrayersByWeeklyReport(FullName);

            #region  小組長回報
            //if (DisplayDateFlag == false)
            //{
            //    m_SmallGroupData.SundayPrayers = new DateTime( 1000, 1, 1 );
            //}
            m_SmallGroupData.Members = new List<Member>();
            #endregion

            #region 新人跟進關懷
            m_NewPersonFollowUpData.SmallGroupLeaderFullName = FullName;
            //m_NewPersonFollowUpData.SundayPrayers = aSelectDate;
            m_NewPersonFollowUpData.SundayPrayers = m_SmallGroupData.SundayPrayers;
            //if (m_SmallGroupData.SundayPrayers.Year != 2000)
            //{
            //    m_NewPersonFollowUpData.SundayPrayersString = m_SmallGroupData.SundayPrayers.ToShortDateString();
            //}
            //else
            //{
            //    m_NewPersonFollowUpData.SundayPrayersString = "";
            //}
            m_NewPersonFollowUpData.Members = new List<Member>();
            #endregion

            #region 組員基本資料維護
            m_AllMemeberData.SmallGroupLeaderFullName = FullName;
            //m_AllMemeberData.SundayPrayers = aSelectDate;
            m_AllMemeberData.SundayPrayers = m_SmallGroupData.SundayPrayers;
            //if (m_SmallGroupData.SundayPrayers.Year != 2000)
            //{
            //    m_AllMemeberData.SundayPrayersString = m_SmallGroupData.SundayPrayers.ToShortDateString();
            //}
            //else
            //{
            //    m_AllMemeberData.SundayPrayersString = "";
            //}

            m_AllMemeberData.Members = new List<Member>();
            #endregion

            #region 把後台的資料轉換成前台的資料結構
            int IdIndex = 0;
            foreach (MemberInfomation aMemberInfomation in m_MemberInfomationPackage.ListMemberInfomation)
            {
                Member aMember = new Member
                {
                    Id = IdIndex,
                    Group = aMemberInfomation.Group,
                    FullName = aMemberInfomation.Name,
                    #region 個人基本資料

                    Phone = aMemberInfomation.Phone,
                    HomePhone = aMemberInfomation.HomePhone,
                    Address = aMemberInfomation.Address,
                    //BirthDate = aMemberInfomation.BirthDate,
                    Industry = aMemberInfomation.Industry,

                    #endregion
                    Status = aMemberInfomation.Identity, // 委身類型
                    SmallGroupName = aMemberInfomation.Group,
                    SectionName = aMemberInfomation.Group,
                    PrayItem = aMemberInfomation.Note,
                    Sunday = aMemberInfomation.SundayPresent,
                    SmallGroup = aMemberInfomation.SmallGroupPresent,
                    #region 新人跟進關懷
                    FollowUpWeek = aMemberInfomation.FollowUpWeek,
                    FollowUpResult = aMemberInfomation.FollowUpResult,
                    FollowUpOption = aMemberInfomation.FollowUpOption,
                    FollowUp = aMemberInfomation.FollowUp,
                    FollowUpNextStep = aMemberInfomation.FollowUpNextStep,
                    FollowUpNote = aMemberInfomation.FollowUpNote,
                    NewComerNote = aMemberInfomation.NewComerNote,
                    #endregion

                    #region 靈修、晨、晚禱
                    SpiritualWork = aMemberInfomation.SpiritualWork, // 靈修次數
                    MorningPray = aMemberInfomation.MorningPray, // 晨禱(家庭祭壇)
                    GeneralCare = aMemberInfomation.GeneralCare, // 晚禱(禱告會次數)
                    #endregion

                    StateID1 = 2,
                    Number1 = 4,
                    StateID2 = 1,
                    Number2 = 2,
                    //Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                    Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                                                              //Picture = "https://tpehoc.speechmessage.com.tw/image/download.aspx?attribute=entityimage&entity=contact&id=66cd8034-953f-e711-80d9-00155d00640b" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees

                };

                // 全部的名單，更新基本資料要用的
                m_AllMemeberData.Members.Add(aMember);

                if (m_MemberInfomationPackage.m_LoginType == "小組長")
                {
                    #region 登入者是小組長
                    // 委身類型客製化
                    if (aMember.Status == "牧師師母" || aMember.Status == "區長" || aMember.Status == "小組長" || aMember.Status == "副組長" || aMember.Status == "小組組員")
                    {
                        // 小組長牧養點名
                        m_SmallGroupData.Members.Add(aMember);
                    }
                    else
                    {
                        if (aMember.Status != "結案" && aMember.Status != "外教會.訪客" && aMember.Status != "幸福BEST")
                        {
                            // 新人跟進關懷
                            m_NewPersonFollowUpData.Members.Add(aMember);
                        }
                    }
                    #endregion
                }
                else
                {
                    #region 登入者是個人回報
                    m_SmallGroupData.Members.Add(aMember);
                    #endregion
                }

                IdIndex++;
            }

            IdIndex = 0;
            m_AssignSmallGroupList.AssignSmallGroupListData.Clear();
            foreach (GroupWeeklyReportGuid aGroupWeeklyReportGuid in m_MemberInfomationPackage.GroupWeeklyReportGuidList)
            {
                String TrimedGroup = this.m_ToolUtilityClass.TrimPresentRate(aGroupWeeklyReportGuid.GroupName);

                AssignSmallGroup aAssignSmallGroup = new AssignSmallGroup
                {
                    ID = IdIndex,
                    Name = TrimedGroup
                };
                m_AssignSmallGroupList.AssignSmallGroupListData.Add(aAssignSmallGroup);
                IdIndex++;

            }
            #endregion

        }
        public void SetSundayPrayersByWeeklyReport(String FullName)
        {
            // 依據週報來決定小組日期
            foreach (GroupWeeklyReportGuid aGroupWeeklyReportGuid in m_MemberInfomationPackage.GroupWeeklyReportGuidList)
            {
                if (aGroupWeeklyReportGuid.SmallGroupLeaderName != null && aGroupWeeklyReportGuid.SmallGroupLeaderName.Contains(FullName))
                {
                    // 找到登入者的小組，因為登入者有可能是區長，所以要確定登入者的小組聚會日期
                    m_SmallGroupData.SundayPrayers = aGroupWeeklyReportGuid.SmallGroupDate;

                    if ( m_SmallGroupData.SundayPrayers.Year == 9999 || m_SmallGroupData.SundayPrayers.Year == 1 )
                    {
                        #region// 表示該週報尚未上傳過，後台還沒有該週報
                        if (this.m_SelectDate.Year != 9999)
                        {
                            // 表示登入的使用有更改過日期，所以網頁要顯示出選擇的日期
                            m_SmallGroupData.SundayPrayers = this.m_SelectDate;
                        }
                        #endregion
                    }
                    else
                    {
                        #region// 表示該週報已經上傳過，後台還已經有該週報
                        if (m_FirstLoginFlag) //是否是首次登入
                        {
                            //是首次登入，小組日期顯示後台周報的小組日期
                            m_SmallGroupData.SundayPrayers = aGroupWeeklyReportGuid.SmallGroupDate;
                        }
                        else
                        {
                            //不是首次登入，表示登入者想要修改小組日期
                            m_SmallGroupData.SundayPrayers = this.m_SelectDate;
                        }
                        #endregion

                    }
                    return;
                }
            }

            // 除錯!
            // 這是個人回報
            if (this.m_SelectDate != null)
            {

                m_SmallGroupData.SundayPrayers = this.m_SelectDate;
            }
            else
            {
                m_SmallGroupData.SundayPrayers = new DateTime(9999, 1, 1);
            }
        }

        public void UploadMemberInfomationPackage()
        {
            UploadData aUploadData = new UploadData();

            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = m_Account,
                Password = m_Password
            };

            DateTime aUploadSmallGroupDate = new DateTime(m_SmallGroupData.SundayPrayers.Year, m_SmallGroupData.SundayPrayers.Month, m_SmallGroupData.SundayPrayers.Day, 0, 0, 0);

            SetSmallGroupDateOfWeeklyReport(m_FullName, aUploadSmallGroupDate);

            aUploadData.UploadMemberDataPackage(aAccountPasswordData, aUploadSmallGroupDate, "主日點名", m_MemberInfomationPackage);
        }

        public void SetSmallGroupDateOfWeeklyReport(String FullName, DateTime SmallGroupDate)
        {
            foreach (GroupWeeklyReportGuid aGroupWeeklyReportGuid in m_MemberInfomationPackage.GroupWeeklyReportGuidList)
            {
                if (aGroupWeeklyReportGuid.SmallGroupLeaderName != null && aGroupWeeklyReportGuid.SmallGroupLeaderName.Contains(FullName))
                {
                    // 找到登入者的小組，因為登入者有可能是區長，所以要確定登入者的小組聚會日期
                    aGroupWeeklyReportGuid.SmallGroupDate = SmallGroupDate;

                    return;
                }
            }
        }

        public void TransferToMemberInfomationPackage(SmallGroupData aGroupDataList)
        {
            int MemberCounter = 0;
            foreach (Member aMember in aGroupDataList.Members)
            {
                MappingMembers(aMember);

                MemberCounter++;
            }
        }
        public void MappingMembers(Member aMember)
        {
            foreach (MemberInfomation aMemberInfomation in m_MemberInfomationPackage.ListMemberInfomation)
            {
                if (aMember.Group == aMemberInfomation.Group && aMember.FullName == aMemberInfomation.Name )
                {
                    aMemberInfomation.Phone = aMember.Phone;
                    aMemberInfomation.HomePhone = aMember.HomePhone;
                    aMemberInfomation.Address = aMember.Address;
                    //aMemberInfomation.BirthDate = aMember.BirthDate;
                    aMemberInfomation.Industry = aMember.Industry;

                    aMemberInfomation.SundayPresent = aMember.Sunday;
                    aMemberInfomation.SmallGroupPresent = aMember.SmallGroup;
                    aMemberInfomation.Note = aMember.PrayItem;
                    aMemberInfomation.FollowUpOption = aMember.FollowUpOption;
                    aMemberInfomation.FollowUp = aMember.FollowUp;
                    aMemberInfomation.FollowUpResult = aMember.FollowUpResult;
                    aMemberInfomation.FollowUpNextStep = aMember.FollowUpNextStep;
                    aMemberInfomation.FollowUpNote = aMember.FollowUpNote;

                    #region 靈修、晨、晚禱
                    aMemberInfomation.SpiritualWork = aMember.SpiritualWork; // 靈修次數
                    aMemberInfomation.MorningPray = aMember.MorningPray;     // 晨禱(家庭祭壇)
                    aMemberInfomation.GeneralCare = aMember.GeneralCare;     // 晚禱(禱告會次數)
                    #endregion

                    break;
                }
            }
        }
        public void AddNewPersonToSmallGroup(PersonFormViewModel aPersonFormViewModel)
        {
            String aGroupName = ConvertGroupName(aPersonFormViewModel.Position);
            Member aMember = new Member
            {
                Id = m_SmallGroupData.Members.Count,
                Group = aGroupName,
                FullName = aPersonFormViewModel.LastName,
                Status = "新朋友",
                SmallGroupName = aGroupName,
                SectionName = aGroupName,
                PrayItem = aPersonFormViewModel.Notes,
                Sunday = false,
                SmallGroup = false,
                StateID1 = 2,
                Number1 = 4,
                StateID2 = 1,
                Number2 = 2,
                //Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                Picture = "../../images/employees/01.png" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees
                                                          //Picture = "https://tpehoc.speechmessage.com.tw/image/download.aspx?attribute=entityimage&entity=contact&id=66cd8034-953f-e711-80d9-00155d00640b" //D:\暫存區\ASP.NET CORE 練習區\DevExtremeAspNetCoreApp1\DevExtremeAspNetCoreApp1\wwwroot\images\employees

            };

            //m_SmallGroupData.Members.Add(aMember);
            m_NewPersonFollowUpData.Members.Add(aMember);

            MemberInfomation aMemberInfomation = new MemberInfomation
            {
                Group = aGroupName,
                Name = aPersonFormViewModel.LastName,
                Phone = aPersonFormViewModel.Phone,
                Address = aPersonFormViewModel.Address,
                Note = aPersonFormViewModel.Notes,
                SundayPresent = false,
                SmallGroupPresent = false
            };

            m_MemberInfomationPackage.ListMemberInfomation.Add(aMemberInfomation);

        }
        public String ConvertGroupName(String GroupNameWithoutPercentage)
        {
            foreach(GroupWeeklyReportGuid aGroupWeeklyReportGuid in m_MemberInfomationPackage.GroupWeeklyReportGuidList)
            {
                if(aGroupWeeklyReportGuid.GroupName.Contains(GroupNameWithoutPercentage))
                {
                    return aGroupWeeklyReportGuid.GroupName;
                }
            }
            return "";
        }

    }
}

