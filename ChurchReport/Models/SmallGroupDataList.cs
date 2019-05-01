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
        public SmallGroupDataList()
        {
            
        }

        public String m_FullName = "";
        //public String m_Account  = "";
        //public String m_Password = "";
        //public DateTime m_SelectDate = new DateTime(2000, 1, 1);// 初始值 2000 表示還沒選
        public DateTime m_SelectDate = DateTime.Now;// 初始值 2000 表示還沒選
        public DateTime m_SundayDate;
        private bool m_FirstLoginFlag;

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

    }
}

