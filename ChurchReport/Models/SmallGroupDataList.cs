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

        public MultiGroupList m_MultiGroupList = new MultiGroupList();

        public void SetupContactIdString(String ContactIdString)
        {
            m_SmallGroupData.SmallGroupLeaderContactId = ContactIdString;
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
        public void AddNewPersonToMember(PersonFormViewModel aPersonFormViewModel)
        {
            String aGroupName = aPersonFormViewModel.Position;
            Member aMember = new Member
            {
                Id = m_SmallGroupData.Members.Count,
                Group = aGroupName,
                FullName = aPersonFormViewModel.LastName,
                Phone = aPersonFormViewModel.Phone,
                HomePhone = aPersonFormViewModel.HomePhone,
                Industry  =aPersonFormViewModel.Industry,
                BirthDate = aPersonFormViewModel.BirthDate,
                Address = aPersonFormViewModel.Address,
                //Gender = aPersonFormViewModel.Gender,
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
            m_AllMemeberData.Members.Add(aMember);
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

