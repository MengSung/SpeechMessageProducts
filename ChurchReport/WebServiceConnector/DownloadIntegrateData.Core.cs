using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.Services;
using ChurchReport.WebServiceConnector.Converters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 主類別（協調者）
    /// 遵循 Linus 代碼原則：保持主類別精簡，委派工作給專門的 partial 類別
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region 欄位與常數

        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");

        private static readonly Regex DigitsOnly = new Regex(@"[^\d]");

        private readonly Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        private bool m_SetIdentityFlag = false;

        // CRM 類型常數
        private const string CRM_TYPE = "DYNAMICS365-9.0";

        // 委身類型自動轉換旗標
        private const bool TRANSFER_IDENTITY_FLAG = false;

        // 過去幾週內出席超過此次數就會改變委身類型 => 小組組員
        private const int WEEK_PERIOD = 8;
        private const int MINIMUM_THRESHOLD = 4;

        // 族系組長能否幫小組長建立週報
        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true;

        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;
        private const int LEVEL_1 = 1;
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5;
        #endregion

        #endregion

        #region 下載資料時所需要的參數

        private DateTime m_Sunday;
        private Entity m_ListEntity;
        private Entity m_ContactEntity;
        private Entity m_WeeklyReportEntity;
        private Guid m_ContactId;
        private string m_LoginType = "";

        #endregion

        #region 轉換器實例

        private IdentityConverter _identityConverter;

        private IdentityConverter IdentityConverterInstance
        {
            get
            {
                if (_identityConverter == null)
                {
                    _identityConverter = new IdentityConverter(
                        m_ToolUtilityClass.m_Crm2011OrganizationService,
                        new MemoryCache(new MemoryCacheOptions())
                    );
                }
                return _identityConverter;
            }
        }

        #endregion

        #region 主要進入點

        /// <summary>
        /// 設定整合資料（主要進入點）
        /// </summary>
        public void SetupIntegrateData(
            string Account, 
            string Password, 
            string LoginType, 
            DateTime aDownloadDate, 
            string ListEntityId, 
            string WeeklyReportEntityId, 
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            this.m_LoginType = LoginType;

            // 計算當週主日日期
            this.m_Sunday = CalculateSunday(aDownloadDate);

            // 設定標頭資料
            this.SetupHeaderData(Account, Password, aDownloadDate, ListEntityId, WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            // 設定牧養資料
            this.SetupShepherdData(ListEntityId, WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            // 設定週報資料
            this.SetupWeeklyReportData(WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            // 設定週報圖表資料
            this.SetupWeeklyReportChartData(ref aListSmallGroupWeeklyReport);
        }

        #endregion

        #region 輔助方法

        /// <summary>
        /// 計算當週主日日期
        /// </summary>
        private DateTime CalculateSunday(DateTime date)
        {
            int dayOfWeek = (int)date.DayOfWeek;

            // 設定主日日期
            // 每周以星期一為第一日
            if (dayOfWeek > 0)
            {
                // 大於 0， 表示星期一到星期六=>下一週的星期日為認定的主日
                return date.AddDays(-dayOfWeek + 7).ToLocalTime();
            }
            else
            {
                // 為 0 = 星期日 (表示 DayOfWeek.Saturday)表示當週星期日為認定的主日
                return date.AddDays(-dayOfWeek).ToLocalTime();
            }

        }

        /// <summary>
        /// 尋找登入使用者
        /// </summary>
        private void FindLoginUser(string Account, string Password)
        {
            if (Account != "LineIdLogin")
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
            }
            else
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
            }

            this.m_ContactId = m_ContactEntity.Id;
        }

        /// <summary>
        /// 移除成員狀態中的數字與空白
        /// </summary>
        private void RemoveNumericAndBlank(List<Member> aMemberList)
        {
            if (aMemberList == null) return;

            foreach (Member aMember in aMemberList)
            {
                aMember.Status = Regex.Replace(aMember.Status, "[0-9]", ""); // 過濾數字
                aMember.Status = aMember.Status.Replace(" ", "");            // 過濾空白
                aMember.Status = aMember.Status.Replace(".", "");            // 過濾逗號
            }
        }

        #endregion

        #region 轉換器委派方法（保持向後相容）

        private string ConvertIndexToIdentity(int identity) => IdentityConverterInstance.IndexToIdentity(identity);
        private string ConvertIndexToSpiritualIdentity(int spiritualIdentity) => IdentityConverterInstance.IndexToSpiritualIdentity(spiritualIdentity);
        private static string ConvertIndexToClearIdentity(int identity) => IdentityConverter.IndexToClearIdentity(identity);
        private static string ConvertIndexToBaptizedSituation(int baptizedSituation) => IdentityConverter.IndexToBaptizedSituation(baptizedSituation);

        private static int ConvertFollowUpWeekPickerToIndex(string followUpWeek) => FollowUpConverter.WeekPickerToIndex(followUpWeek);
        private static int ConvertFollowUpResultPickerToIndex(string followUpResult) => FollowUpConverter.ResultPickerToIndex(followUpResult);
        private static int ConvertFollowUpNextStepPickerToIndex(string followUpNextStep) => FollowUpConverter.NextStepPickerToIndex(followUpNextStep);
        private static int ConvertFollowUpOptionToIndex(string followUpOption) => FollowUpConverter.OptionToIndex(followUpOption);
        private static string ConvertIndexToFollowUpWeekPicker(int optionValue) => FollowUpConverter.IndexToWeekPicker(optionValue);
        private static string ConvertIndexToFollowUpResultPicker(int optionValue) => FollowUpConverter.IndexToResultPicker(optionValue);
        private static string ConvertIndexToFollowUpNextStepPicker(int optionValue) => FollowUpConverter.IndexToNextStepPicker(optionValue);
        private static string ConvertIndexToFollowUpOptionPicker(int optionValue) => FollowUpConverter.IndexToOptionPicker(optionValue);
        private static string ConvertIndexToTopic(int optionValue) => FollowUpConverter.IndexToTopic(optionValue);
        private static string ConvertNumberToFollowUpWeekPicker(int weekNumber) => FollowUpConverter.NumberToWeekPicker(weekNumber);
        private static int ConvertNumberToWeekIndex(int weekNumber) => FollowUpConverter.NumberToWeekIndex(weekNumber);
        private string ConvertIndexToVisit(int visit)
        {
            try
            {
                var optionSetService = new OptionSetMetadataService(
                    m_ToolUtilityClass.m_Crm2011OrganizationService,
                    null,
                    new MemoryCache(new MemoryCacheOptions()));

                return optionSetService.GetOptionSetText("new_present_record", "new_visit", visit);
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion
    }
}
