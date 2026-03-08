using ChurchReport.ViewModels;
using ChurchReport.WebServiceConnector;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

namespace ChurchReport.Models
{
    public class ListSmallGroupWeeklyReport
    {
        #region 初始化
        public ListSmallGroupWeeklyReport()
        {
            ModifyFlag = false;
            m_UploadIntegrateData = new UploadIntegrateData();

            //GroupArray.Add("以利亞");
            //GroupArray.Add("耶穌基督");
            //GroupArray.Add("耶和華");
            //m_WeeklyReportViewModel = new WeeklyReportViewModel
            // {
            //     WeeklyReportData = "AAAAA",
            //     WeeklyReportAnalysis = "BBBBBB"
            // };

        }
        #endregion
        #region 資料區
        #region 參數資料
        // 個別小組長點名的畫面所需要的資料
        public bool LoadFlag { get; set; }
        public String ListEntityId { get; set; } // 小組 ID

        public String WeeklyReportEntityId;//{ get; set; } // 週報 ID
        public String ListEntityName { get; set; } // 小組名稱
        public String LoginType { get; set; } // 回報型式: 小組長 OR 個人回報
        public String GroupType { get; set; } = "一般小組"; // 小組型式: "一般小組" OR "幸福小組"
        public String SmallGroupLeaderContactId { get; set; } // 小組長 ID
        public String SmallGroupLeaderFullName { get; set; } // 小組長姓名
        public DateTime SundayPrayers { get; set; } // 小組日期
        public String SundayPeriod { get; set; }   // 提醒小組長回報的期間
        public SmallGroupDataList m_SmallGroupDataList = new SmallGroupDataList();// 包含 3 個SmallGroupData ( 小組牧養、新人跟進關懷、基本資料維護)，而每個又包含一個Members陣列

        public String HappyWeekIndex; //{ get; set; } // 幸福小組週次
        public String HappyWeekTopic; //{ get; set; } // 幸福小組主題

        public String WeeklyReportData; //{ get; set; } // 小組日誌
        public String WeeklyReportAnalysis; //{ get; set; } // 小組分析報告
        public bool PauseCheckBox; //{ get; set; } // 小組是否暫停
        // 長條圖表資料
        public ChartDataList m_WeeklyReportChart { get; set; }
        public bool ModifyFlag { get; set; }
        public UploadIntegrateData m_UploadIntegrateData;

        // 表單是個人回報需要用到"暫時"傳遞資料用的資料結構
        public PersonalReportViewModel m_PersonalReportViewModel = new PersonalReportViewModel();

        //public WeeklyReportViewModel m_WeeklyReportViewModel { get; set; }
        //public WeeklyReportViewModel m_WeeklyReportViewModel = new WeeklyReportViewModel
        //{
        //    WeeklyReportData = "AAAAA",
        //    WeeklyReportAnalysis = "BBBBBB"
        //};

        // 個人回報但是沒有加入小組聚會統計主日日期相關的個人聚會與靈修記錄
        public Guid m_PresentRecordWithNoSmallGroupId = new Guid();
        public Entity m_PresentRecordWithNoSmallGroupEntity = new Entity();

        public List<String> GroupArray { get; set; } = new List<string>();     //換小組要用到的小組清單

        #endregion
        #endregion
        public void UploadIntegrateData( DateTime aSelectedDate, String Account, String Password, String LoginType, SmallGroupData aSmallGroupData, String aWeeklyReportData, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
        {
            WeeklyReportData = aWeeklyReportData;
            HappyWeekIndex = HappyWeekIndex;
            HappyWeekTopic = HappyWeekTopic;
            PauseCheckBox = PauseCheckBox;

            //ChurchReport.Models.ListSmallGroupWeeklyReport
            m_UploadIntegrateData.UploadData(aSelectedDate, Account, Password, LoginType, GroupType, ListEntityId, ref WeeklyReportEntityId, SundayPrayers, aSmallGroupData, ref WeeklyReportData, ref WeeklyReportAnalysis, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

            return;
        }

        /// <summary>
        /// 非同步版本的 UploadIntegrateData。
        /// 內部會呼叫 UploadIntegrateData.UploadDataAsync，並在完成後將 ref 值回寫到本物件。
        /// 保留同步版本以維持向下相容性。
        /// </summary>
        public async System.Threading.Tasks.Task UploadIntegrateDataAsync(
            DateTime aSelectedDate,
            String Account,
            String Password,
            String LoginType,
            SmallGroupData aSmallGroupData,
            String aWeeklyReportData,
            String HappyWeekIndex,
            String HappyWeekTopic,
            bool PauseCheckBox,
            System.Threading.CancellationToken cancellationToken = default)
        {
            WeeklyReportData = aWeeklyReportData;
            HappyWeekIndex = HappyWeekIndex;
            HappyWeekTopic = HappyWeekTopic;
            PauseCheckBox = PauseCheckBox;

            // 使用 UploadIntegrateData 類別提供的非同步 wrapper
            var result = await m_UploadIntegrateData.UploadDataAsync(
                aSelectedDate,
                Account,
                Password,
                LoginType,
                GroupType,
                ListEntityId,
                SundayPrayers,
                aSmallGroupData,
                WeeklyReportData,
                WeeklyReportAnalysis,
                HappyWeekIndex,
                HappyWeekTopic,
                PauseCheckBox,
                WeeklyReportEntityId,
                cancellationToken).ConfigureAwait(false);

            // 將結果回寫到本物件
            if (result != null)
            {
                WeeklyReportEntityId = result.WeeklyReportEntityId;
                WeeklyReportData = result.WeeklyReportData;
                WeeklyReportAnalysis = result.WeeklyReportAnalysis;
            }
        }
        public void DeleteMemberData(String Account, String Password, Member aMemberToBeDeleted)
        {
            m_UploadIntegrateData.DeleteMember(Account, Password, ListEntityId, aMemberToBeDeleted);
        }
        public void SetPersonalReportViewModel(ref ToolUtilityClass m_ToolUtilityClass, Entity aLoginContact)
        {
            try
            {
                if (m_SmallGroupDataList.m_AllMemeberData.Members != null)
                {
                    // 個人回報但是有加入小組
                    if (m_SmallGroupDataList.m_AllMemeberData.Members[0] != null)
                    {
                        m_PersonalReportViewModel.SundayPrayers = SundayPrayers;
                        m_PersonalReportViewModel.GroupName = m_SmallGroupDataList.m_AllMemeberData.Members[0].Group;
                        m_PersonalReportViewModel.FullName = m_SmallGroupDataList.m_AllMemeberData.Members[0].FullName;
                        m_PersonalReportViewModel.SundayPresent = m_SmallGroupDataList.m_AllMemeberData.Members[0].Sunday;
                        m_PersonalReportViewModel.SmallGroupPresent = m_SmallGroupDataList.m_AllMemeberData.Members[0].SmallGroup;
                        m_PersonalReportViewModel.SpiritualWork = m_SmallGroupDataList.m_AllMemeberData.Members[0].SpiritualWork;
                        m_PersonalReportViewModel.MorningPray = m_SmallGroupDataList.m_AllMemeberData.Members[0].MorningPray;
                        m_PersonalReportViewModel.GeneralCare = m_SmallGroupDataList.m_AllMemeberData.Members[0].GeneralCare;
                        m_PersonalReportViewModel.PrayItem = m_SmallGroupDataList.m_AllMemeberData.Members[0].PrayItem;
                    }
                }
                else
                {
                    // 個人回報但是沒有加入小組
                    SetPersonalReportNoSmallGroup(ref m_ToolUtilityClass, ref aLoginContact);

                    //throw new Exception("您沒加入任何小組，所以無法回報喔!");
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetPersonalReportNoSmallGroup(ref ToolUtilityClass m_ToolUtilityClass, ref Entity aLoginContact)
        {
            try
            {
                // 個人回報但是沒有加入小組

                // 取得與聚會統計主日日期相關的個人聚會與靈修記錄
                //EntityCollection aPresentRecordCollection = m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndSundayDate(m_ToolUtilityClass.GetEntityStringAttribute(ref aLoginContact, "fullname"), aLoginContact.Id.ToString(), DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek));

                SetPersonalReportViewModelWithNoSmallGroup(ref m_ToolUtilityClass, ref aLoginContact, m_ToolUtilityClass.RetrievePresentRecordByFetchXmlAndSundayDate(m_ToolUtilityClass.GetEntityStringAttribute(ref aLoginContact, "fullname"), aLoginContact.Id.ToString(), DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek)));

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetPersonalReportViewModelWithNoSmallGroup(ref ToolUtilityClass m_ToolUtilityClass, ref Entity aLoginContact, EntityCollection aPresentRecordCollection)
        {
            try
            {
                // 個人回報但是沒有加入小組

                // 取得與聚會統計主日日期相關的個人聚會與靈修記錄
                if (aPresentRecordCollection.Entities.Count > 0)
                {
                    #region 有找到與聚會統計主日日期相關的個人聚會與靈修記錄
                    Entity aRetrievedPresenRecordEntity = m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordCollection.Entities[0].Id);

                    SetPersonalReportViewModel(ref m_ToolUtilityClass, ref aLoginContact, aRetrievedPresenRecordEntity);

                    m_PresentRecordWithNoSmallGroupEntity = aRetrievedPresenRecordEntity;

                    m_PresentRecordWithNoSmallGroupId = aRetrievedPresenRecordEntity.Id;
                    #endregion
                }
                else
                {
                    #region 沒找到與聚會統計主日日期相關的個人聚會與靈修記錄

                    // 建立新增一個與聚會統計主日日期相關的個人聚會與靈修記錄
                    m_PresentRecordWithNoSmallGroupEntity = CreatePresentRecordWithNoSmallGroup(ref m_ToolUtilityClass, ref aLoginContact);

                    SetPersonalReportViewModel(ref m_ToolUtilityClass, ref aLoginContact, m_PresentRecordWithNoSmallGroupEntity);

                    m_PresentRecordWithNoSmallGroupId = m_PresentRecordWithNoSmallGroupEntity.Id;

                    #endregion
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetPersonalReportViewModel(ref ToolUtilityClass m_ToolUtilityClass, ref Entity aLoginContact, Entity aPresentRecord)
        {
            try
            {
                // 個人回報但是沒有加入小組

                // 取得與聚會統計主日日期相關的個人聚會與靈修記錄
                //m_PersonalReportViewModel.ID = aPresentRecordCollection.Entities[0].Id.ToString();
                DateTime aSmallGroupDate = m_ToolUtilityClass.GetEntityDateTimeAttribute(aPresentRecord, "new_group_date");
                m_PersonalReportViewModel.SundayPrayers = aSmallGroupDate.Year == 1 ? DateTime.Now : aSmallGroupDate;

                m_PersonalReportViewModel.GroupName = "未入組";
                m_PersonalReportViewModel.FullName = m_ToolUtilityClass.GetEntityStringAttribute(ref aLoginContact, "fullname");
                m_PersonalReportViewModel.SundayPresent = m_ToolUtilityClass.GetEntityIntAttribute(aPresentRecord, "new_sunday_present_this_week") > 0 ? true : false;
                m_PersonalReportViewModel.SmallGroupPresent = m_ToolUtilityClass.GetEntityIntAttribute(aPresentRecord, "new_group_present_this_week") > 0 ? true : false;
                m_PersonalReportViewModel.SpiritualWork = m_ToolUtilityClass.GetEntityIntAttribute(aPresentRecord, "new_spiritual_work");
                m_PersonalReportViewModel.MorningPray = m_ToolUtilityClass.GetEntityIntAttribute(aPresentRecord, "new_morning_pray");
                m_PersonalReportViewModel.GeneralCare = m_ToolUtilityClass.GetEntityIntAttribute(aPresentRecord, "new_general_care");
                m_PersonalReportViewModel.PrayItem = m_ToolUtilityClass.GetEntityStringAttribute(aPresentRecord, "new_explanation");
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SavePersonalReportForm(ref ToolUtilityClass m_ToolUtilityClass, PersonalReportViewModel aPersonalReportViewModel)
        {
            try
            {
                if (m_PresentRecordWithNoSmallGroupEntity != null)
                {
                    // 個人回報但是沒有加入小組

                    // 儲存上傳與聚會統計主日日期相關的個人聚會與靈修記錄
                    m_ToolUtilityClass.SetEntityIntAttribute(ref m_PresentRecordWithNoSmallGroupEntity, "new_sunday_present_this_week", aPersonalReportViewModel.SundayPresent == true ? 1 : 0);
                    m_ToolUtilityClass.SetEntityIntAttribute(ref m_PresentRecordWithNoSmallGroupEntity, "new_group_present_this_week", aPersonalReportViewModel.SmallGroupPresent == true ? 1 : 0);
                    m_ToolUtilityClass.SetEntityIntAttribute(ref m_PresentRecordWithNoSmallGroupEntity, "new_spiritual_work", aPersonalReportViewModel.SpiritualWork);
                    m_ToolUtilityClass.SetEntityIntAttribute(ref m_PresentRecordWithNoSmallGroupEntity, "new_morning_pray", aPersonalReportViewModel.MorningPray);
                    m_ToolUtilityClass.SetEntityIntAttribute(ref m_PresentRecordWithNoSmallGroupEntity, "new_general_care", aPersonalReportViewModel.GeneralCare);
                    m_ToolUtilityClass.SetEntityStringAttribute(ref m_PresentRecordWithNoSmallGroupEntity, "new_explanation", aPersonalReportViewModel.PrayItem);

                    m_ToolUtilityClass.UpdateEntity(m_PresentRecordWithNoSmallGroupEntity);
                }
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private Entity CreatePresentRecordWithNoSmallGroup(ref ToolUtilityClass m_ToolUtilityClass, ref Entity aLoginContact)
        {
            try
            {
                // 這是新建立的個人聚會與靈修記錄
                Entity aPresentRecord = new Entity("new_present_record");

                // 設定個人聚會與靈修記錄相關屬性
                this.SetupPresentRecordEntityAttributes(aPresentRecord, ref aLoginContact, ref m_ToolUtilityClass);

                // 新增個人聚會與靈修記錄
                Guid aPresentRecordId = m_ToolUtilityClass.CreateEntity(aPresentRecord);

                //指派負責人
                //this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                //取得並回傳新建的聚會與靈修記錄
                return m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private void SetupPresentRecordEntityAttributes(Entity aPresentRecord, ref Entity aContactEntity, ref ToolUtilityClass m_ToolUtilityClass)
        {
            try
            {
                #region 設定名稱
                String PresentRecordName = m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "fullname") + "_" + String.Format("{0:00}/{1:00}/{2:00} 出席紀錄", DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
                m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", PresentRecordName);
                //this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", PresentRecordName);
                #endregion
                #region 設定姓名
                // 找到組員ID
                Guid aContactEntityId = aContactEntity.Id;
                m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntityId);
                #endregion
                #region 設定歸零
                m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);   // 設定主日出席
                m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);             // 設定主日出席率
                m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);    // 設定小組出席
                m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);        // 設定小組出席率
                m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", 0);              // 設定幸福小組出席
                #endregion

                #region 設定行動電話
                m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", m_ToolUtilityClass.GetEntityStringAttribute(aContactEntity, "mobilephone"));
                #endregion

                #region 設定主日聚會日期
                #region 先根據日期尋找當週主日日期
                // 依設定檔的每週第一日規則，集中計算目前週次對應的主日日期。
                DateTime aSunday = ChurchReport.Services.SundayCalculator.CalculateSunday(
                    DateTime.Now,
                    ChurchReport.Services.WeeklyScheduleProvider.FirstDayOfWeek);
                #endregion

                m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", aSunday);
                #endregion

                #region 設定小組聚會日期
                m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", DateTime.Now);
                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        public void GetPersonalReportViewModelResult(PersonalReportViewModel aPersonalReportViewModel)
        {
            if (m_SmallGroupDataList.m_AllMemeberData.Members[0] != null && m_SmallGroupDataList.m_AllMemeberData.Members != null)
            {
                m_SmallGroupDataList.m_AllMemeberData.Members[0].Sunday = m_PersonalReportViewModel.SundayPresent = aPersonalReportViewModel.SundayPresent;
                m_SmallGroupDataList.m_AllMemeberData.Members[0].SmallGroup = m_PersonalReportViewModel.SmallGroupPresent = aPersonalReportViewModel.SmallGroupPresent;
                m_SmallGroupDataList.m_AllMemeberData.Members[0].SpiritualWork = m_PersonalReportViewModel.SpiritualWork = aPersonalReportViewModel.SpiritualWork;
                m_SmallGroupDataList.m_AllMemeberData.Members[0].MorningPray = m_PersonalReportViewModel.MorningPray = aPersonalReportViewModel.MorningPray;
                m_SmallGroupDataList.m_AllMemeberData.Members[0].GeneralCare = m_PersonalReportViewModel.GeneralCare = aPersonalReportViewModel.GeneralCare;
                m_SmallGroupDataList.m_AllMemeberData.Members[0].PrayItem = m_PersonalReportViewModel.PrayItem = aPersonalReportViewModel.PrayItem;
            }
            else
            {
                m_PersonalReportViewModel.SundayPresent = m_PersonalReportViewModel.SundayPresent = aPersonalReportViewModel.SundayPresent;
                m_PersonalReportViewModel.SmallGroupPresent = m_PersonalReportViewModel.SmallGroupPresent = aPersonalReportViewModel.SmallGroupPresent;
                m_PersonalReportViewModel.SpiritualWork = m_PersonalReportViewModel.SpiritualWork = aPersonalReportViewModel.SpiritualWork;
                m_PersonalReportViewModel.MorningPray = m_PersonalReportViewModel.MorningPray = aPersonalReportViewModel.MorningPray;
                m_PersonalReportViewModel.GeneralCare = m_PersonalReportViewModel.GeneralCare = aPersonalReportViewModel.GeneralCare;
                m_PersonalReportViewModel.PrayItem = m_PersonalReportViewModel.PrayItem = aPersonalReportViewModel.PrayItem;
            }
        }
    }

}

