using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
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
            aListSmallGroupWeeklyReport.SundayPeriod = $"小組日期對應到主日期間是: {m_Sunday.AddDays(-1).ToLocalTime().ToShortDateString()} ~ {m_Sunday.AddDays(5).ToLocalTime().ToShortDateString()}";
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
                this.SetNewPersonFollowUpData(ref aListSmallGroupWeeklyReport);
            }
            else
            {
                this.SetHappyGroupData(ref aListSmallGroupWeeklyReport);
            }

            // 取得所有小組清單
            EntityCollection aListEntityCollection = m_ToolUtilityClass.RetrieveListByFetchXml();
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
        /// </summary>
        private void SortAndCleanMemberStatus(ref ListSmallGroupWeeklyReport report)
        {
            // 排序委身類型
            if (report.m_SmallGroupDataList.m_AllMemeberData?.Members != null)
                report.m_SmallGroupDataList.m_AllMemeberData.Members = report.m_SmallGroupDataList.m_AllMemeberData.Members.OrderBy(o => o.Status).ToList();

            if (report.m_SmallGroupDataList.m_SmallGroupData?.Members != null)
                report.m_SmallGroupDataList.m_SmallGroupData.Members = report.m_SmallGroupDataList.m_SmallGroupData.Members.OrderBy(o => o.Status).ToList();

            if (report.m_SmallGroupDataList.m_NewPersonFollowUpData?.Members != null)
                report.m_SmallGroupDataList.m_NewPersonFollowUpData.Members = report.m_SmallGroupDataList.m_NewPersonFollowUpData.Members.OrderBy(o => o.Status).ToList();

            if (report.m_SmallGroupDataList.m_HappyGroup?.Members != null)
                report.m_SmallGroupDataList.m_HappyGroup.Members = report.m_SmallGroupDataList.m_HappyGroup.Members.OrderBy(o => o.Status).ToList();

            // 去除數字、空白、逗號
            RemoveNumericAndBlank(report.m_SmallGroupDataList.m_AllMemeberData?.Members);
            RemoveNumericAndBlank(report.m_SmallGroupDataList.m_SmallGroupData?.Members);
            RemoveNumericAndBlank(report.m_SmallGroupDataList.m_NewPersonFollowUpData?.Members);
            RemoveNumericAndBlank(report.m_SmallGroupDataList.m_HappyGroup?.Members);
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

            // 查詢過去兩個月的週報
            EntityCollection GroupWeeklyReportEntityCollection = this.m_ToolUtilityClass.QueryWeeklyReportBeforeTowMonthOfSunday(this.m_Sunday, this.m_ListEntity.Id);

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
        /// </summary>
        private void SetSmallGroupData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData = new SmallGroupData
            {
                Members = new List<Member>()
            };

            foreach (Member aMember in aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members)
            {
                if (!aMember.Status.Contains("新朋友") && 
                    !aMember.Status.Contains("未入組") && 
                    !aMember.Status.Contains("外教會") && 
                    !aMember.Status.Contains("結案"))
                {
                    aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members.Add(aMember);
                }
            }
        }

        /// <summary>
        /// 設定新人跟進資料（只包含新朋友和未入組）
        /// </summary>
        private void SetNewPersonFollowUpData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData = new SmallGroupData
            {
                Members = new List<Member>()
            };

            foreach (Member aMember in aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members)
            {
                if (aMember.Status.Contains("新朋友") || aMember.Status.Contains("未入組"))
                {
                    aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_NewPersonFollowUpData.Members.Add(aMember);
                }
            }
        }

        /// <summary>
        /// 設定幸福小組資料（包含所有成員）
        /// </summary>
        private void SetHappyGroupData(ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)
        {
            aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup = new SmallGroupData
            {
                Members = new List<Member>()
            };

            foreach (Member aMember in aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData.Members)
            {
                aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_HappyGroup.Members.Add(aMember);
            }
        }

        #endregion
    }
}
