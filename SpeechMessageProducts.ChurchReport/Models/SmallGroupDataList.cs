// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/SmallGroupDataList.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class SmallGroupDataList
// 主要成員：SetupContactIdString、SetSmallGroupDateOfWeeklyReport、TransferToMemberInfomationPackage、MappingMembers、AddNewPersonToMember
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ToolUtilityNameSpace、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、Microsoft.Xrm.Sdk.Client
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

        // 幸福小組
        public SmallGroupData m_HappyGroup = new SmallGroupData();

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
                    // 找到登入者的小組，因為登入者有可能是小家長，所以要確定登入者的小組聚會日期
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
                    aMemberInfomation.EquipmentStatus = aMember.EquipmentStatus;    // 裝備狀態
                    aMemberInfomation.SpiritualIdentity = aMember.SpiritualIdentity;// 受洗狀態
                    aMemberInfomation.BaptizedSituation = aMember.BaptizedSituation;// 洗禮狀態(長老教會專用)

                    aMemberInfomation.SundayPresent = aMember.Sunday;
                    aMemberInfomation.SmallGroupPresent = aMember.SmallGroup;
                    aMemberInfomation.Note = aMember.PrayItem;
                    aMemberInfomation.FollowUpOption = aMember.FollowUpOption;
                    aMemberInfomation.FollowUp = aMember.FollowUp;
                    aMemberInfomation.FollowUpResult = aMember.FollowUpResult;
                    aMemberInfomation.FollowUpNextStep = aMember.FollowUpNextStep;
                    aMemberInfomation.FollowUpNote = aMember.FollowUpNote;

                    #region 靈修、晨、晚禱
                    aMemberInfomation.SpiritualWork = aMember.SpiritualWork; // 讀經次數
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
            if (aGroupName != null)
            {
                if (aGroupName.Contains("幸福"))
                {
                    Member aMember = new Member
                    {
                        PresentRecordId = aPersonFormViewModel.PresentRecordId,
                        Id = m_HappyGroup.Members.Count,
                        Group = aGroupName,
                        FullName = aPersonFormViewModel.LastName,
                        Phone = aPersonFormViewModel.Phone,
                        HomePhone = aPersonFormViewModel.HomePhone,
                        Industry = aPersonFormViewModel.Industry,
                        EquipmentStatus = aPersonFormViewModel.EquipmentStatus,
                        SpiritualIdentity = aPersonFormViewModel.SpiritualIdentity,// 受洗狀態
                        BaptizedSituation = aPersonFormViewModel.BaptizedSituation,// 洗禮狀態(長老教會專用)
                        //BirthDate = aPersonFormViewModel.BirthDate,
                        Address = aPersonFormViewModel.Address,
                        //Gender = aPersonFormViewModel.Gender,
                        Status = "幸福BEST",
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
                    m_HappyGroup.DisplayFlag = true;
                    m_HappyGroup.Members.Add(aMember);
                    m_AllMemeberData.Members.Add(aMember);
                }
                else
                {
                    Member aMember = new Member
                    {
                        PresentRecordId = aPersonFormViewModel.PresentRecordId,
                        Id = m_SmallGroupData.Members.Count,
                        Group = aGroupName,
                        FullName = aPersonFormViewModel.LastName,
                        Phone = aPersonFormViewModel.Phone,
                        HomePhone = aPersonFormViewModel.HomePhone,
                        Industry = aPersonFormViewModel.Industry,
                        EquipmentStatus = aPersonFormViewModel.EquipmentStatus,
                        SpiritualIdentity = aPersonFormViewModel.SpiritualIdentity,// 受洗狀態
                        BaptizedSituation = aPersonFormViewModel.BaptizedSituation,// 洗禮狀態(長老教會專用)
                        //BirthDate = aPersonFormViewModel.BirthDate,
                        Address = aPersonFormViewModel.Address,
                        //Gender = aPersonFormViewModel.Gender,
                        Status = aPersonFormViewModel.CustomerTypeCode,
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

                    if (aPersonFormViewModel.CustomerTypeCode == "小組組員")
                    {
                        // 新增新朋友時，導入階段希望先設定為"小組組員"
                        m_SmallGroupData.DisplayFlag = true;
                        m_SmallGroupData.Members.Add(aMember);
                    }
                    else
                    {
                        // 新增新朋友時，導入成功後設定為"新朋友"
                        m_NewPersonFollowUpData.DisplayFlag = true;
                        m_NewPersonFollowUpData.Members.Add(aMember);
                    }
                    // 加入至"維護基本資料"用
                    m_AllMemeberData.Members.Add(aMember);

                }
            }
            else
            {
                // 個人回報，而且沒加入小組
            }
        }
    }
}

