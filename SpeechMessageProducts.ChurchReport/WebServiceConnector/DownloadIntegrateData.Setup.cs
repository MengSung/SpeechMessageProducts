// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DownloadIntegrateData.Setup.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DownloadIntegrateData
// 主要成員：SetupHeaderData、SetupShepherdData、SortAndCleanMemberStatus、SetupWeeklyReportData、SetupHappyGroupWeeklyData、SetupCommonWeeklyData、SetupWeeklyReportChartData、InitializeChartData、SetSmallGroupData、SetNewPersonFollowUpData
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Text、ChurchReport.Models、ChurchReport.Services、Microsoft.Extensions.Caching.Memory、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChurchReport.Models;
using ChurchReport.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 整合資料下載服務 - 設定相關方法
    /// </summary>
    public partial class DownloadIntegrateData
    {
        #region 標頭設定

        /// <summary>
        /// 設定標頭資料
        /// </summary>
        public void SetupHeaderData(
            string Account,
            string Password,
            DateTime aDownloadDate,
            string ListEntityId,
            string WeeklyReportEntityId,
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 找登入使用者及其ID
            FindLoginUser(Account, Password);
            if (m_ContactId == Guid.Empty)
            {
                return; // 沒找到就回傳
            }

            aListSmallGroupWeeklyReport.LoadFlag = true;
            this.m_ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(ListEntityId));

            aListSmallGroupWeeklyReport.ListEntityName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_ListEntity, "listname");
            aListSmallGroupWeeklyReport.GroupType = aListSmallGroupWeeklyReport.ListEntityName.Contains("幸福") ? "幸福小組" : "一般小組";

            aListSmallGroupWeeklyReport.WeeklyReportEntityId = WeeklyReportEntityId;
            if (!string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                this.m_WeeklyReportEntity = this.m_ToolUtilityClass.RetrieveEntity("new_group_present_weekly_report", new Guid(WeeklyReportEntityId));
            }

            aListSmallGroupWeeklyReport.LoginType = this.m_LoginType;
            aListSmallGroupWeeklyReport.SmallGroupLeaderFullName = m_ToolUtilityClass.GetEntityLookupDisplayName(ref m_ListEntity, "new_contact_family_leader_list");
            aListSmallGroupWeeklyReport.SundayPrayers = aDownloadDate;

            // 「小組日期對應到主日期間」不能再硬編碼為「主日前 6 天到主日」，
            // 必須依照 appsettings.json 的 WeeklySchedule:每週的第一日 動態決定。
            // 例如：
            // - 星期一起始：區間為 星期一 ~ 星期日
            // - 星期六起始：區間為 星期六 ~ 星期五
            // - 星期日起始：區間為 星期日 ~ 星期六
            DateTime weekStart = SundayCalculator.CalculateWeekStart(m_Sunday, WeeklyScheduleProvider.FirstDayOfWeek);
            DateTime weekEnd = SundayCalculator.CalculateWeekEnd(m_Sunday, WeeklyScheduleProvider.FirstDayOfWeek);
            aListSmallGroupWeeklyReport.SundayPeriod = $"小組日期對應到主日期間是: {weekStart.ToShortDateString()} ~ {weekEnd.ToShortDateString()}";

            aListSmallGroupWeeklyReport.SmallGroupLeaderContactId = m_ContactId.ToString();
            aListSmallGroupWeeklyReport.SmallGroupLeaderFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_ContactEntity, "fullname");
        }

        #endregion

        #region 牧養資料設定

        /// <summary>
        /// 設定牧養資料
        /// </summary>
        public void SetupShepherdData(
            string ListEntityId,
            string WeeklyReportEntityId,
            ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 初始化 SmallGroupDataList
            aListSmallGroupWeeklyReport.m_SmallGroupDataList = new SmallGroupDataList();

            // 取得所有成員資料
            this.GetAllMemeberDataList(ListEntityId, WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);

            // 根據小組類型設定資料
            if (!aListSmallGroupWeeklyReport.GroupType.Contains("幸福"))
            {
                this.SetSmallGroupData(ref aListSmallGroupWeeklyReport);
                // SetNewPersonFollowUpData 已整合入 SetSmallGroupData 的單次遍歷
            }
            else
            {
                this.SetHappyGroupData(ref aListSmallGroupWeeklyReport);
            }

            // ? 極速：所有小組名稱清單幾乎不變，快取 30 分鐘省去 CRM 查詢
            // Session 安全：小組名稱為系統公開資料，所有使用者共享相同清單
            const string listCacheKey = "AllGroupList_v1";
            if (!_optionSetCache.TryGetValue(listCacheKey, out EntityCollection aListEntityCollection))
            {
                aListEntityCollection = m_ToolUtilityClass.RetrieveListByFetchXml();
                _optionSetCache.Set(listCacheKey, aListEntityCollection,
                    new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(30)));
            }
            aListSmallGroupWeeklyReport.GroupArray.Clear();
            foreach (Entity aList in aListEntityCollection.Entities)
            {
                aListSmallGroupWeeklyReport.GroupArray.Add(m_ToolUtilityClass.GetEntityStringAttribute(aList, "listname"));
            }

            // 排序委身類型並清理格式
            SortAndCleanMemberStatus(ref aListSmallGroupWeeklyReport);
        }

        /// <summary>
        /// 排序並清理成員狀態
        /// ? 極速：List.Sort() 原地排序取代 OrderBy().ToList()，省去 4 次 List 建構
        /// </summary>
        private static void SortAndCleanMemberStatus(ref ListSmallGroupWeeklyReport report)
        {
            var dataList = report?.m_SmallGroupDataList;
            if (dataList == null)
            {
                return;
            }

            // 此階段會原地排序並逐筆改寫 Status；必須與 SaveIntegrate 快照及其他前景寫入
            // 使用同一份 Session 資料圖同步根。鎖內只做記憶體排序／字串正規化，絕不執行 CRM、
            // HTTP 或其他 I/O，避免延長鎖定時間或讓不同使用者共用可變資料。
            dataList.ExecuteSynchronized(() =>
            {
                // 原地排序，無需建立新 List
                dataList.m_AllMemeberData?.Members?.Sort(static (a, b) => string.CompareOrdinal(a.Status, b.Status));
                dataList.m_SmallGroupData?.Members?.Sort(static (a, b) => string.CompareOrdinal(a.Status, b.Status));
                dataList.m_NewPersonFollowUpData?.Members?.Sort(static (a, b) => string.CompareOrdinal(a.Status, b.Status));
                dataList.m_HappyGroup?.Members?.Sort(static (a, b) => string.CompareOrdinal(a.Status, b.Status));

                // 去除數字、空白、逗號
                RemoveNumericAndBlank(dataList.m_AllMemeberData?.Members);
                RemoveNumericAndBlank(dataList.m_SmallGroupData?.Members);
                RemoveNumericAndBlank(dataList.m_NewPersonFollowUpData?.Members);
                RemoveNumericAndBlank(dataList.m_HappyGroup?.Members);
            });
        }

        #endregion

        #region 週報資料設定

        /// <summary>
        /// 設定週報資料
        /// </summary>
        public void SetupWeeklyReportData(string WeeklyReportEntityId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            if (aListSmallGroupWeeklyReport.GroupType == "幸福小組")
            {
                SetupHappyGroupWeeklyData(WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);
            }

            SetupCommonWeeklyData(WeeklyReportEntityId, ref aListSmallGroupWeeklyReport);
        }

        /// <summary>
        /// 設定幸福小組週報資料
        /// </summary>
        private void SetupHappyGroupWeeklyData(string WeeklyReportEntityId, ref ListSmallGroupWeeklyReport report)
        {
            if (!string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                report.HappyWeekIndex = m_ToolUtilityClass.GetEntityStringAttribute(this.m_WeeklyReportEntity, "new_weekly_index");
                report.HappyWeekTopic = ConvertIndexToTopic(m_ToolUtilityClass.GetOptionSetAttribute(this.m_WeeklyReportEntity, "new_topic"));
            }
            else
            {
                report.HappyWeekIndex = "";
                report.HappyWeekTopic = "";
            }
        }

        /// <summary>
        /// 設定通用週報資料（小組日誌、分析及暫停）
        /// </summary>
        private void SetupCommonWeeklyData(string WeeklyReportEntityId, ref ListSmallGroupWeeklyReport report)
        {
            if (!string.IsNullOrEmpty(WeeklyReportEntityId))
            {
                report.WeeklyReportData = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_WeeklyReportEntity, "new_memo");
                report.WeeklyReportAnalysis = this.m_ToolUtilityClass.GetEntityStringAttribute(ref this.m_WeeklyReportEntity, "new_sunday_present_report");
                report.PauseCheckBox = this.m_ToolUtilityClass.GetOptionSetAttribute(ref this.m_WeeklyReportEntity, "new_weekly_report_status") == 100000002;
            }
            else
            {
                report.WeeklyReportData = "";
                report.WeeklyReportAnalysis = "";
                report.PauseCheckBox = false;
            }
        }

        #endregion

        #region 週報圖表資料設定

        /// <summary>
        /// 設定週報圖表資料
        /// </summary>
        public void SetupWeeklyReportChartData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 初始化圖表資料
            InitializeChartData(ref aListSmallGroupWeeklyReport);

            // ? 極速：圖表資料依「小組ID + 主日」快取 15 分鐘，所有使用者共享
            // Session 安全：圖表僅含出席人數統計，無個人資料
            string chartCacheKey = $"WeeklyReportChart_{this.m_ListEntity.Id:N}_{this.m_Sunday:yyyyMMdd}";
            if (!_optionSetCache.TryGetValue(chartCacheKey, out EntityCollection GroupWeeklyReportEntityCollection))
            {
                GroupWeeklyReportEntityCollection = this.m_ToolUtilityClass.QueryWeeklyReportBeforeTowMonthOfSunday(this.m_Sunday, this.m_ListEntity.Id);
                _optionSetCache.Set(chartCacheKey, GroupWeeklyReportEntityCollection,
                    new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(15)));
            }

            // 填充圖表資料
            foreach (Entity aWeeklyReporEntity in GroupWeeklyReportEntityCollection.Entities)
            {
                int aSundayNumber = this.m_ToolUtilityClass.GetEntityIntAttribute(aWeeklyReporEntity, "new_sunday_present_number");
                int aSmallNumber = this.m_ToolUtilityClass.GetEntityIntAttribute(aWeeklyReporEntity, "new_small_group_number");

                aListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList.Add(new ChartData
                {
                    WeeklyReportEntityId = aWeeklyReporEntity.Id.ToString(),
                    SundayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aWeeklyReporEntity, "new_sunday_date").ToLocalTime().ToShortDateString(),
                    SundayNumber = Math.Max(aSundayNumber, 0),
                    SmallNumber = Math.Max(aSmallNumber, 0),
                });
            }
        }

        /// <summary>
        /// 初始化圖表資料結構
        /// </summary>
        private void InitializeChartData(ref ListSmallGroupWeeklyReport report)
        {
            if (report.m_WeeklyReportChart == null)
            {
                report.m_WeeklyReportChart = new ChartDataList
                {
                    m_ChartDataList = new List<ChartData>()
                };
            }
            else
            {
                if (report.m_WeeklyReportChart.m_ChartDataList != null)
                {
                    report.m_WeeklyReportChart.m_ChartDataList.Clear();
                }
                else
                {
                    report.m_WeeklyReportChart.m_ChartDataList = new List<ChartData>();
                }
            }
        }

        #endregion

        #region 小組資料分類

        /// <summary>
        /// 設定小組牧養資料（過濾掉新朋友和未入組）
        /// ? 極速：與 SetNewPersonFollowUpData 合併為單次遍歷，省去第二次迭代
        /// </summary>
        private void SetSmallGroupData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // CRM 查詢已在先前流程完成；分類與兩個集合發布必須是同一份短暫記憶體交易，
            // 讓 SaveIntegrate 快照只能讀到完整的舊分類或完整的新分類。
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.RebuildSmallGroupAndNewPersonDataFromAllMembers();
        }

        /// <summary>
        /// 設定新人跟進資料（已整合入 SetSmallGroupData，保留以維持相容性）
        /// </summary>
        private static void SetNewPersonFollowUpData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 已在 SetSmallGroupData 的單次遍歷中完成，無需重複處理
        }

        /// <summary>
        /// 設定幸福小組資料（包含所有成員）
        /// </summary>
        private void SetHappyGroupData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            // 只處理已下載完成的記憶體資料；不得把 CRM 或其他 I/O 放入資料圖同步區間。
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.RebuildHappyGroupDataFromAllMembers();
        }

        #endregion
    }
}
