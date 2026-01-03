using Microsoft.AspNetCore.Mvc;
using System;
using System.Globalization;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - 日期更新與幸福小組週次
    /// </summary>
    public partial class SmallGroupController
    {
        #region 幸福小組週次更新

        /// <summary>
        /// 更新幸福小組週次
        /// </summary>
        [HttpPost]
        public IActionResult UpdateHappyWeekIndex(string HappyWeekIndex)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.HappyWeekIndex = HappyWeekIndex;
                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateHappyWeekIndex");
            }
        }

        /// <summary>
        /// 更新幸福小組主題
        /// </summary>
        [HttpPost]
        public IActionResult UpdateHappyWeekTopic(string HappyWeekTopic)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.HappyWeekTopic = HappyWeekTopic;
                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateHappyWeekTopic");
            }
        }

        #endregion

        #region 日期更新

        /// <summary>
        /// 更新多小組檢視的日期
        /// </summary>
        [HttpGet]
        public IActionResult UpdateDate(string SelectedDate)
        {
            try
            {
                if (!DateTime.TryParseExact(SelectedDate, 
                    new[] { "yyyy/M/d", "yyyy/MM/dd", "yyyy-MM-dd" }, 
                    CultureInfo.InvariantCulture, 
                    DateTimeStyles.None, 
                    out DateTime selectedDateTime))
                {
                    return Json(new { success = false, message = "日期格式錯誤" });
                }

                ClearMultiGroupCache();
                System.Diagnostics.Debug.WriteLine($"[UpdateDate] 已清除多小組快取，新日期: {selectedDateTime:yyyy/M/d}");

                InMemoryContext.ListManager.m_SelectDate = selectedDateTime;

                InMemoryContext.ListManager.SetupListManager(
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password,
                    selectedDateTime);

                return Json(new { 
                    success = true, 
                    message = "日期更新成功" 
                });
            }
            catch (Exception e)
            {
                return Json(new { 
                    success = false, 
                    message = $"日期更新失敗: {e.Message}" 
                });
            }
        }

        /// <summary>
        /// 更新綜合報表日期（保持當前選擇的小組）
        /// </summary>
        [HttpGet]
        public IActionResult UpdateIntegrateDate(string SelectedDate)
        {
            try
            {
                if (!DateTime.TryParseExact(SelectedDate, 
                    new[] { "yyyy/M/d", "yyyy/MM/dd", "yyyy-MM-dd" }, 
                    CultureInfo.InvariantCulture, 
                    DateTimeStyles.None, 
                    out DateTime selectedDateTime))
                {
                    return Json(new { success = false, message = "日期格式錯誤" });
                }

                string currentListId = InMemoryContext.ListManager.ActiveListId;
                
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 當前小組 ID: {currentListId}");
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 更新日期: {selectedDateTime:yyyy/M/d}");

                ClearMultiGroupCache();
                ClearIntegrateCache(currentListId);
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 已清除快取");
                
                InMemoryContext.ListManager.m_SelectDate = selectedDateTime;

                InMemoryContext.ListManager.SetupListManager(
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password,
                    selectedDateTime);

                if (!string.IsNullOrEmpty(currentListId))
                {
                    InMemoryContext.ListManager.ActiveListId = currentListId;
                    System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 恢復小組 ID: {currentListId}");
                }
                else
                {
                    currentListId = InMemoryContext.ListManager.ActiveListId;
                    System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 使用新的小組 ID: {currentListId}");
                }

                InMemoryContext.ListManager.SetupIntegrateData(currentListId);

                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 完成載入小組: {currentListId}");

                return Json(new { 
                    success = true, 
                    ActiveListId = currentListId,
                    message = "日期更新成功" 
                });
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 錯誤: {e.Message}");
                System.Diagnostics.Debug.WriteLine($"[UpdateIntegrateDate] 堆疊: {e.StackTrace}");
                
                return Json(new { 
                    success = false, 
                    message = $"日期更新失敗: {e.Message}" 
                });
            }
        }

        #endregion
    }
}
