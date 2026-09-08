// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/ListSmallGroupWeeklyReport.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class ListSmallGroupWeeklyReport
// 主要成員：UploadIntegrateData、UploadIntegrateDataAsync、DeleteMemberData、SetPersonalReportViewModel、SetPersonalReportNoSmallGroup、SetPersonalReportViewModelWithNoSmallGroup、SavePersonalReportForm、CreatePresentRecordWithNoSmallGroup、SetupPresentRecordEntityAttributes、GetPersonalReportViewModelResult
// 引用命名空間：ChurchReport.ViewModels、ChurchReport.WebServiceConnector、Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Linq、System.Threading.Tasks、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

        /// <summary>
        /// 建立供背景上傳專用的週報副本。
        ///
        /// m_SmallGroupDataList 透過其短鎖深拷貝建立，三組 Members 與每個 Member
        /// 均不會與前景 Session 共享。只複製 UploadIntegrateDataAsync 會讀取的純量
        /// 中繼資料；建構式會建立新的上傳器。圖表、個人回報 ViewModel、CRM Entity 與
        /// GroupArray 不屬於此上傳資料流，刻意不複製以避免背景工作保留前景物件圖。
        /// 副本的最長生命週期是上傳與清理完成前，且不保存 HttpContext、Session 或租約資源。
        /// </summary>
        /// <returns>完全隔離的背景上傳週報實例。</returns>
        public ListSmallGroupWeeklyReport CreateBackgroundUploadCopy()
        {
            var copy = new ListSmallGroupWeeklyReport
            {
                LoadFlag = LoadFlag,
                ListEntityId = ListEntityId,
                WeeklyReportEntityId = WeeklyReportEntityId,
                ListEntityName = ListEntityName,
                LoginType = LoginType,
                GroupType = GroupType,
                SmallGroupLeaderContactId = SmallGroupLeaderContactId,
                SmallGroupLeaderFullName = SmallGroupLeaderFullName,
                SundayPrayers = SundayPrayers,
                SundayPeriod = SundayPeriod,
                m_SmallGroupDataList = m_SmallGroupDataList?.CreateIsolatedSnapshot() ?? new SmallGroupDataList(),
                HappyWeekIndex = HappyWeekIndex,
                HappyWeekTopic = HappyWeekTopic,
                WeeklyReportData = WeeklyReportData,
                WeeklyReportAnalysis = WeeklyReportAnalysis,
                PauseCheckBox = PauseCheckBox,
                ModifyFlag = ModifyFlag,
                m_PresentRecordWithNoSmallGroupId = m_PresentRecordWithNoSmallGroupId
            };

            return copy;
        }

        /// <summary>
        /// 建立供 Controller/序列化使用的 detached read copy。除資料圖外也複製圖表與小組名稱，
        /// 讓讀取端不會持有 Session holder 的可變 List；複製完成後來源可安全被下一世代交換。
        /// </summary>
        /// <returns>不含原始 Member、ChartData 與 GroupArray 參考的讀取快照。</returns>
        internal ListSmallGroupWeeklyReport CreateDetachedReadCopy()
        {
            // 讀取快照只需要一次 deep copy；不可先呼叫背景上傳副本再覆寫資料圖，
            // 否則每個 Grid/Chart AJAX 都會對同一批 Members 複製兩次，增加配置與 GC 壓力。
            // 此方法不會攜帶 Session、HttpContext、CRM client 或上傳器的可變參考；
            // 來源資料圖只在 CreateDetachedReadSnapshot 的短鎖內讀取，回傳後即與來源隔離。
            var copy = new ListSmallGroupWeeklyReport
            {
                LoadFlag = LoadFlag,
                ListEntityId = ListEntityId,
                WeeklyReportEntityId = WeeklyReportEntityId,
                ListEntityName = ListEntityName,
                LoginType = LoginType,
                GroupType = GroupType,
                SmallGroupLeaderContactId = SmallGroupLeaderContactId,
                SmallGroupLeaderFullName = SmallGroupLeaderFullName,
                SundayPrayers = SundayPrayers,
                SundayPeriod = SundayPeriod,
                m_SmallGroupDataList = m_SmallGroupDataList?.CreateDetachedReadSnapshot() ?? new SmallGroupDataList(),
                HappyWeekIndex = HappyWeekIndex,
                HappyWeekTopic = HappyWeekTopic,
                WeeklyReportData = WeeklyReportData,
                WeeklyReportAnalysis = WeeklyReportAnalysis,
                PauseCheckBox = PauseCheckBox,
                ModifyFlag = ModifyFlag,
                m_PresentRecordWithNoSmallGroupId = m_PresentRecordWithNoSmallGroupId,
                GroupArray = GroupArray == null ? new List<string>() : new List<string>(GroupArray)
            };
            copy.m_WeeklyReportChart = new ChartDataList
            {
                m_ChartDataList = m_WeeklyReportChart?.m_ChartDataList == null
                    ? new List<ChartData>()
                    : m_WeeklyReportChart.m_ChartDataList.Select(item => new ChartData
                    {
                        WeeklyReportEntityId = item.WeeklyReportEntityId,
                        SundayDate = item.SundayDate,
                        SundayNumber = item.SundayNumber,
                        SmallNumber = item.SmallNumber
                    }).ToList()
            };
            return copy;
        }

        public void UploadIntegrateData(DateTime aSelectedDate, String Account, String Password, String LoginType, SmallGroupData aSmallGroupData, String aWeeklyReportData, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)
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
            // 個人回報的六個欄位必須一次更新目前 Session 資料圖；否則 SaveIntegrate 逐欄複製時可能
            // 取得不存在於任何前景時間點的混合 Member。此區不做 CRM、HTTP 或 Task I/O。
            m_SmallGroupDataList.ExecuteSynchronized(() =>
            {
                var members = m_SmallGroupDataList.m_AllMemeberData.Members;
                if (members != null && members.Count > 0 && members[0] != null)
                {
                    members[0].Sunday = m_PersonalReportViewModel.SundayPresent = aPersonalReportViewModel.SundayPresent;
                    members[0].SmallGroup = m_PersonalReportViewModel.SmallGroupPresent = aPersonalReportViewModel.SmallGroupPresent;
                    members[0].SpiritualWork = m_PersonalReportViewModel.SpiritualWork = aPersonalReportViewModel.SpiritualWork;
                    members[0].MorningPray = m_PersonalReportViewModel.MorningPray = aPersonalReportViewModel.MorningPray;
                    members[0].GeneralCare = m_PersonalReportViewModel.GeneralCare = aPersonalReportViewModel.GeneralCare;
                    members[0].PrayItem = m_PersonalReportViewModel.PrayItem = aPersonalReportViewModel.PrayItem;
                }
                else
                {
                    m_PersonalReportViewModel.SundayPresent = aPersonalReportViewModel.SundayPresent;
                    m_PersonalReportViewModel.SmallGroupPresent = aPersonalReportViewModel.SmallGroupPresent;
                    m_PersonalReportViewModel.SpiritualWork = aPersonalReportViewModel.SpiritualWork;
                    m_PersonalReportViewModel.MorningPray = aPersonalReportViewModel.MorningPray;
                    m_PersonalReportViewModel.GeneralCare = aPersonalReportViewModel.GeneralCare;
                    m_PersonalReportViewModel.PrayItem = aPersonalReportViewModel.PrayItem;
                }
            });
        }
    }

}
