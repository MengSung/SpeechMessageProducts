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
        public DateTime m_SundayDate ;
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");
        //static ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        // 小組長點名
        public SmallGroupData m_SmallGroupData = new SmallGroupData();

        // 新人跟進關懷
        public SmallGroupData m_NewPersonFollowUpData = new SmallGroupData();

        // 全部的名單，更新基本資料要用的
        public SmallGroupData m_AllMemeberData = new SmallGroupData();

        public MemberInfomationPackage m_MemberInfomationPackage;

        public void SetupContactIdString(String ContactIdString)
        {
            m_SmallGroupData.SmallGroupLeaderContactId = ContactIdString;
        }
        public void SetupSmallGroupData(String FullName, String Account, String Password, DateTime SundayDate)
        {
            m_FullName = FullName;
            m_Account = Account;
            m_Password = Password;
            DownloadData aDownloader = new DownloadData();
            AccountPasswordData aAccountPasswordData = new AccountPasswordData
            {
                Account = Account,
                Password = Password
            };

            // 從雲端後台下載下來小組點名資料
            m_MemberInfomationPackage = aDownloader.GetMemberDataPackage(SundayDate, aAccountPasswordData);

            m_SmallGroupData.SmallGroupLeaderFullName = FullName;
            m_SmallGroupData.SundayPrayers = m_SundayDate = SundayDate;
            m_SmallGroupData.Members = new List<Member>();

            m_NewPersonFollowUpData.SmallGroupLeaderFullName = FullName;
            m_NewPersonFollowUpData.SundayPrayers = m_SundayDate = SundayDate;
            m_NewPersonFollowUpData.Members = new List<Member>();

            m_AllMemeberData.SmallGroupLeaderFullName = FullName;
            m_AllMemeberData.SundayPrayers = m_SundayDate = SundayDate;
            m_AllMemeberData.Members = new List<Member>();

            int IdIndex = 0;
            foreach (MemberInfomation aMemberInfomation in m_MemberInfomationPackage.ListMemberInfomation)
            {
                Member aMember = new Member
                {
                    Id= IdIndex,
                    Group = aMemberInfomation.Group,
                    FullName = aMemberInfomation.Name,
                    #region 個人基本資料

                    Phone = aMemberInfomation.Phone,
                    HomePhone = aMemberInfomation.HomePhone,
                    Address = aMemberInfomation.Address,
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

                if ( m_MemberInfomationPackage.m_LoginType == "小組長")
                {
                    #region 登入者是小組長
                    if (aMember.Status == "區牧長" || aMember.Status == "區牧" || aMember.Status == "區長" || aMember.Status == "小組長" || aMember.Status == "實習小組長" || aMember.Status == "小組組員")
                    {
                        // 小組長牧養點名
                        m_SmallGroupData.Members.Add(aMember);
                    }
                    else
                    {
                        if (aMember.Status != "結案")
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
            AssignSmallGroupList.AssignSmallGroupListData.Clear();
            foreach (GroupWeeklyReportGuid aGroupWeeklyReportGuid in m_MemberInfomationPackage.GroupWeeklyReportGuidList)
            {
                String TrimedGroup = this.m_ToolUtilityClass.TrimPresentRate(aGroupWeeklyReportGuid.GroupName);

                AssignSmallGroup aAssignSmallGroup = new AssignSmallGroup
                {
                    ID = IdIndex,
                    Name = TrimedGroup
                };
                AssignSmallGroupList.AssignSmallGroupListData.Add(aAssignSmallGroup);
                IdIndex++;

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

            aUploadData.UploadMemberDataPackage(aAccountPasswordData, m_SmallGroupData.SundayPrayers, "主日點名", m_MemberInfomationPackage);
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
                    aMemberInfomation.Industry = aMember.Industry;

                    aMemberInfomation.SundayPresent = aMember.Sunday;
                    aMemberInfomation.SmallGroupPresent = aMember.SmallGroup;
                    aMemberInfomation.Note = aMember.PrayItem;
                    aMemberInfomation.FollowUpOption = aMember.FollowUpOption;
                    aMemberInfomation.FollowUp = aMember.FollowUp;
                    aMemberInfomation.FollowUpResult = aMember.FollowUpResult;
                    aMemberInfomation.FollowUpNextStep = aMember.FollowUpNextStep;
                    aMemberInfomation.FollowUpNote = aMember.FollowUpNote;
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

