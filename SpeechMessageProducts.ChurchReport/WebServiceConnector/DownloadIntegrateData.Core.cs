// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DownloadIntegrateData.Core.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DownloadIntegrateData
// 主要成員：SetupIntegrateData、CalculateSunday、FindLoginUser、RemoveNumericAndBlank、KeepDigitsOnly、CountDigits、StripChars、ConvertIndexToIdentity、ConvertIndexToSpiritualIdentity、ConvertIndexToClearIdentity
// 引用命名空間：System、System.Collections.Generic、System.Text.RegularExpressions、ChurchReport.Models、ChurchReport.Models.CrmTransmitModule、ChurchReport.Services、ChurchReport.WebServiceConnector.Converters、Microsoft.Extensions.Caching.Memory
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

        /// <summary>
        /// 接收 operation-local CRM service 的整合資料下載入口。
        ///
        /// <para>
        /// 傳入的 service 是呼叫端 lease owner 借出的資源；此方法不能將它保存到本類別、
        /// ListManager、ToolUtility、Factory、static 欄位或 cache，也不能 Dispose、Close、
        /// Abort 或歸還它。這些限制可防止 session 快取的 ListManager 讓不同使用者、
        /// profile 或 connector generation 共用可變 client state。
        /// </para>
        ///
        /// <para>
        /// 本類別的 legacy partial 仍包含直接使用 Factory 共用 ToolUtility service 欄位的
        /// 路徑。在那些路徑全部改為顯式參數前，讓借用 service 進入原版流程會造成 silent
        /// fallback 與 cross-operation reuse。本 overload 因此在第一個 CRM 呼叫前固定
        /// fail closed；不得以暫存欄位、AsyncLocal 或 finally 清空來假裝隔離。
        /// </para>
        /// </summary>
        /// <param name="Account">既有登入流程提供的受保護帳號資料；不會記錄或快取。</param>
        /// <param name="Password">既有登入流程提供的受保護驗證資料；不會記錄或快取。</param>
        /// <param name="LoginType">已驗證的既有登入類型。</param>
        /// <param name="aDownloadDate">目前操作的週期日期。</param>
        /// <param name="ListEntityId">已授權的名單識別。</param>
        /// <param name="WeeklyReportEntityId">既有週報識別；不會被寫入共用狀態。</param>
        /// <param name="aListSmallGroupWeeklyReport">只屬於目前操作回應的輸出模型。</param>
        /// <param name="organizationService">呼叫端借用、仍由其 owner 釋放的 CRM service。</param>
        /// <exception cref="ArgumentNullException">當未提供 operation-local service 時擲回。</exception>
        /// <exception cref="InvalidOperationException">當下載鏈尚不能完整保證 service 隔離時擲回。</exception>
        public void SetupIntegrateData(
            string Account,
            string Password,
            string LoginType,
            DateTime aDownloadDate,
            string ListEntityId,
            string WeeklyReportEntityId,
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport,
            IOrganizationService organizationService)
        {
            ArgumentNullException.ThrowIfNull(organizationService);

            // 個人回報與其他 legacy 登入型態仍會走到 ToolUtility-only partial；必須在任何登入、
            // 名單、週報或圖表 CRM I/O 之前拒絕。這可避免未完成 operation-local 隔離的呼叫先讀取
            // 借用 service 後，再因後段 fallback 造成跨 Session／profile 的 service 或資料混用。
            // service 的 fault eviction、lease 歸還與 Dispose 始終仍由呼叫端 owner 負責。
            if (!string.Equals(LoginType, "小組長", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "operation-local 下載目前只支援已完成隔離驗證的小組長唯讀路徑；其他登入型態已在 CRM I/O 前拒絕回落至共用 ToolUtility。");
            }

            // 先把所有輸出建立在本次呼叫的區域物件；任何 CRM 例外都不會污染呼叫端既有報表，
            // 更不會把 borrowed service 寫入可被 Session 重用的 DownloadIntegrateData 欄位。
            var operationReport = new ListSmallGroupWeeklyReport();
            SetupHeaderData(
                Account,
                Password,
                aDownloadDate,
                ListEntityId,
                WeeklyReportEntityId,
                LoginType,
                ref operationReport,
                organizationService);

            SetupOperationLocalLeaderMembers(ListEntityId, WeeklyReportEntityId, ref operationReport, organizationService);
            SetupWeeklyReportData(WeeklyReportEntityId, ref operationReport, organizationService);
            SetupWeeklyReportChartData(ref operationReport, organizationService);

            // 所有唯讀 SDK 呼叫成功後才以單一 reference assignment 提交；這是此同步入口唯一
            // 改動 caller output 的位置，故失敗與 timeout 不會留下半成品或前次 service 的資料。
            aListSmallGroupWeeklyReport = operationReport;
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
        /// ? 極速版：使用 char 迴圈取代 Regex，零 Regex 引擎開銷
        /// </summary>
        private static void RemoveNumericAndBlank(List<Member> aMemberList)
        {
            if (aMemberList == null) return;

            foreach (Member aMember in aMemberList)
            {
                aMember.Status = StripChars(aMember.Status, stripDigits: true, stripSpaces: true, stripDots: true);
            }
        }

        /// <summary>
        /// ? 極速：只保留數字字元（用於電話號碼），完全避免 Regex 開銷
        /// </summary>
        private static string KeepDigitsOnly(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // 快速路徑：如果全部都是數字就直接回傳
            bool allDigits = true;
            for (int i = 0; i < input.Length; i++)
            {
                if (!char.IsDigit(input[i]))
                {
                    allDigits = false;
                    break;
                }
            }
            if (allDigits) return input;

            return string.Create(CountDigits(input), input, static (span, src) =>
            {
                int pos = 0;
                for (int i = 0; i < src.Length; i++)
                {
                    if (char.IsDigit(src[i]))
                        span[pos++] = src[i];
                }
            });
        }

        private static int CountDigits(string s)
        {
            int count = 0;
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) count++;
            return count;
        }

        /// <summary>
        /// ? 極速：一次遍歷移除指定類型字元，避免多次 String.Replace 造成多次配置
        /// </summary>
        private static string StripChars(string input, bool stripDigits, bool stripSpaces, bool stripDots)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // 快速路徑：計算需要保留的字元數
            int keepCount = 0;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                bool skip = (stripDigits && char.IsDigit(c))
                         || (stripSpaces && c == ' ')
                         || (stripDots && c == '.');
                if (!skip) keepCount++;
            }

            if (keepCount == input.Length) return input; // 無需修改
            if (keepCount == 0) return string.Empty;

            return string.Create(keepCount, (input, stripDigits, stripSpaces, stripDots), static (span, state) =>
            {
                int pos = 0;
                string src = state.input;
                for (int i = 0; i < src.Length; i++)
                {
                    char c = src[i];
                    bool skip = (state.stripDigits && char.IsDigit(c))
                             || (state.stripSpaces && c == ' ')
                             || (state.stripDots && c == '.');
                    if (!skip)
                        span[pos++] = c;
                }
            });
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
        /// 進程級靜態快取（所有使用者共享同一份）
        ///
        /// ? Session Leakage 完整審計（最後更新：2025-06）
        /// ══════════════════════════════════════════════════════════════
        ///
        /// 【允許放入此快取的資料類型】
        /// 1. OptionSet_{entity}_{attr}       → CRM 欄位下拉選項（Schema 定義）
        /// 2. OptionSetReverse_{entity}_{attr} → 上述反向對應
        /// 3. AllGroupList_v1                  → 所有小組名稱清單（系統公開）
        /// 4. WeeklyReportChart_{listId}_{date}→ 出席人數統計（聚合數據）
        ///
        /// 【禁止放入此快取的資料類型】
        /// ? 個人出席紀錄（FollowUpHistory_*）  → 含個人牧養資料
        /// ? 個人聯絡資訊（Contact Entity）      → 含姓名/電話/地址
        /// ? 任何 Entity 含使用者可辨識資訊       → Session Leakage 風險
        /// ? 可變的 EntityCollection（會被後續修改）→ 資料污染風險
        ///
        /// 【判斷準則】
        /// 此快取只能存放「所有使用者看到完全相同結果」的系統級資料。
        /// 如果資料會因使用者身份/權限不同而有差異，絕對不可快取。
        /// ══════════════════════════════════════════════════════════════
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
