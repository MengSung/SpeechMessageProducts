using ChurchReport.Models;
using ChurchReport.Tools;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Mvc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - 資料載入 API
    /// </summary>
    public partial class SmallGroupController
    {
        #region 資料載入 API

        /// <summary>
        /// 載入整合式頁面的小組成員資料
        /// </summary>
        [HttpGet]
        public object LoadIntegrate(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                EnsureCorrectUserData();

                EnsureIntegrateDataLoaded(id);

                var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_SmallGroupData.Members;

                return DataSourceLoader.Load(tasks, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "LoadIntegrate");
            }
        }

        /// <summary>
        /// 載入圖表資料
        /// </summary>
        [HttpGet]
        [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "WeeklyReportId" })]
        public object GetChartDataList(string WeeklyReportId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                EnsureCorrectUserData();

                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport == null ||
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_WeeklyReportChart == null ||
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_WeeklyReportChart.m_ChartDataList == null)
                {
                    return DataSourceLoader.Load(new List<ChartData>(), loadOptions);
                }

                var chartData = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_WeeklyReportChart.m_ChartDataList;

                return DataSourceLoader.Load(chartData, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "GetChartDataList");
            }
        }

        /// <summary>
        /// 載入多小組圓餅圖資料（含快取）
        /// </summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public object GetMultiGroupChartDataList(string WeeklyReportId, DataSourceLoadOptions loadOptions)
        {
            try
            {
                var selectedDate = InMemoryContext.ListManager.m_SelectDate;
                var account = InMemoryContext.ListManager.m_Account ?? "guest";
                var cacheKey = $"{CACHE_KEY_MULTI_CHART}{selectedDate:yyyyMMdd}_{account}";
                
                if (_memoryCache != null && _memoryCache.TryGetValue(cacheKey, out object cachedData) && cachedData is List<MultiGroupChartData> cachedChartData)
                {
                    System.Diagnostics.Debug.WriteLine($"[GetMultiGroupChartDataList] 從快取讀取: {cacheKey}");
                    return DataSourceLoader.Load(cachedChartData, loadOptions);
                }
                
                System.Diagnostics.Debug.WriteLine($"[GetMultiGroupChartDataList] 快取未命中: {cacheKey}");

                if (InMemoryContext.ListManager.m_MultiGroupChartDataList == null ||
                    InMemoryContext.ListManager.m_MultiGroupChartDataList.m_MultiGroupChartDataList == null)
                {
                    return DataSourceLoader.Load(new List<MultiGroupChartData>(), loadOptions);
                }

                var chartData = InMemoryContext.ListManager.m_MultiGroupChartDataList.m_MultiGroupChartDataList;

                if (chartData != null && chartData.Any())
                {
                    _memoryCache?.Set(cacheKey, chartData, CreateCacheOptions());
                    System.Diagnostics.Debug.WriteLine($"[GetMultiGroupChartDataList] 已快取 {chartData.Count} 筆");
                }

                return DataSourceLoader.Load(chartData, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "GetMultiGroupChartDataList");
            }
        }

        /// <summary>
        /// 載入多小組列表資料（含快取）
        /// </summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public object AssignSmallGroupGet(string id, DataSourceLoadOptions loadOptions)
        {
            try
            {
                var selectedDate = InMemoryContext.ListManager.m_SelectDate;
                var account = InMemoryContext.ListManager.m_Account ?? "guest";
                var cacheKey = $"{CACHE_KEY_MULTI_GRID}{selectedDate:yyyyMMdd}_{account}";
                
                if (_memoryCache != null && _memoryCache.TryGetValue(cacheKey, out object cachedData) && cachedData is List<WeeklyReportRecord> cachedRecords)
                {
                    System.Diagnostics.Debug.WriteLine($"[AssignSmallGroupGet] 從快取讀取: {cacheKey}");
                    return DataSourceLoader.Load(cachedRecords, loadOptions);
                }
                
                System.Diagnostics.Debug.WriteLine($"[AssignSmallGroupGet] 快取未命中: {cacheKey}");

                if (InMemoryContext.ListManager.m_MultiGroupList == null ||
                    InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData == null)
                {
                    return DataSourceLoader.Load(new List<WeeklyReportRecord>(), loadOptions);
                }

                var weeklyReportRecords = InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData;

                if (weeklyReportRecords != null && weeklyReportRecords.Any())
                {
                    _memoryCache?.Set(cacheKey, weeklyReportRecords, CreateCacheOptions());
                    System.Diagnostics.Debug.WriteLine($"[AssignSmallGroupGet] 已快取 {weeklyReportRecords.Count} 筆");
                }

                return DataSourceLoader.Load(weeklyReportRecords, loadOptions);
            }
            catch (Exception e)
            {
                return HandleError(e, "AssignSmallGroupGet");
            }
        }

        #endregion
    }
}
