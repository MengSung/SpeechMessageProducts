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

        // ? 效能優化：加入 RegexOptions.Compiled，JIT 編譯正則表達式以加速匹配
        private static readonly Regex DigitsOnly = new Regex(@"[^\d]", RegexOptions.Compiled);

        private readonly Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        private bool m_SetIdentityFlag = false;

        // ? 效能修復：CRM 類型常數統一為 "DYNAMICS365"，與所有比較一致
        private const string CRM_TYPE = "DYNAMICS365";

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
                    // ? 效能優化：共享靜態 _optionSetCache，避免每個 DownloadIntegrateData 實例各自冷啟動
                    // Session 安全：_optionSetCache 僅存放 CRM Schema Metadata（不含使用者資料）
                    _identityConverter = new IdentityConverter(
                        m_ToolUtilityClass.m_Crm2011OrganizationService,
                        _optionSetCache
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
            // 集中由 SundayCalculator 依設定檔的每週第一日規則計算主日，
            // 避免不同檔案各自維護硬編碼邏輯。
            return SundayCalculator.CalculateSunday(date, WeeklyScheduleProvider.FirstDayOfWeek);
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
        /// ? 效能優化：重用靜態編譯的 DigitsOnly Regex，避免每次迭代建立新 Regex 實例
        /// </summary>
        private void RemoveNumericAndBlank(List<Member> aMemberList)
        {
            if (aMemberList == null) return;

            foreach (Member aMember in aMemberList)
            {
                aMember.Status = DigitsOnly.Replace(aMember.Status, ""); // 過濾數字（重用靜態 Regex）
                aMember.Status = aMember.Status.Replace(" ", "");        // 過濾空白
                aMember.Status = aMember.Status.Replace(".", "");        // 過濾逗號
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
        /// <summary>
        /// OptionSet 查詢用共享快取（避免每次呼叫建立新 MemoryCache，降低 GC 壓力）
        /// 
        /// ? Session 安全性分析（已驗證安全）：
        /// 此快取為 static，所有使用者共享同一份，但不會造成 Session Leakage，原因如下：
        /// 
        /// 1. 快取鍵格式: "OptionSet_{entityName}_{attributeName}"
        ///    例如: "OptionSet_new_present_record_new_visit"
        ///    → 鍵中不含任何使用者 ID、Session ID 或個人資訊
        /// 
        /// 2. 快取內容: Dictionary&lt;string, int&gt;（OptionSet 顯示文字 → 整數值）
        ///    例如: { "探訪" → 1, "電話關懷" → 2 }
        ///    → 這是 CRM 欄位的 Schema 定義（Metadata），屬於系統級資料
        ///    → 所有使用者看到的 OptionSet 選項完全相同
        ///    → 不含任何使用者個人資料、登入狀態或 Session 資訊
        /// 
        /// 3. 結論: A 登入 → B 登入，兩人共享的只有「下拉選單的選項文字」，
        ///    這是 CRM 實體定義的一部分，與使用者身份無關，安全無虞。
        /// </summary>
        private static readonly MemoryCache _optionSetCache = new MemoryCache(new MemoryCacheOptions());

        private string ConvertIndexToVisit(int visit)
        {
            try
            {
                // ? 效能優化：重用靜態快取，避免每次呼叫都 new MemoryCache (原本每次建立新的無法快取任何東西)
                var optionSetService = new OptionSetMetadataService(
                    m_ToolUtilityClass.m_Crm2011OrganizationService,
                    null,
                    _optionSetCache);

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
