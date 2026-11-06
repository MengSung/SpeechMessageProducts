/*
 * 檔案整體說明
 *
 * 目的與定位：小組管理控制器 - 日期更新與幸福小組週次
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupController。
 * 主要操作入口包含 UpdateHappyWeekIndex、UpdateHappyWeekTopic、UpdateDate、UpdateIntegrateDate。
 *
 * 使用邊界與維護：
 * 本檔與同名控制器的其他partial共用一次請求的依賴及狀態；授權、Session與資料範圍必須沿既有伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Cache.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Core.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.IntegrateView.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
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

                // SetupListManager 會在 ListManager 的 Session 專屬 gate 內一次更新日期、登入 scope
                // 與可見清單；不可先在鎖外寫 m_SelectDate，否則另一個 AJAX 可能用新日期搭配舊清單。
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

                // 日期、可見清單、原小組再授權與整合候選發布必須是同一個原子操作。
                // preferredListId 只是切換前提示；ListManager 會在新日期的 server-side 可見清單中
                // 再次驗證，失效時採用新的 ActiveListId，避免 caller 值越權或跨日期沿用舊快照。
                var snapshot = InMemoryContext.ListManager.ReloadDateAndGetIntegrateDetachedRead(
                    InMemoryContext.ListManager.m_Account,
                    InMemoryContext.ListManager.m_Password,
                    selectedDateTime,
                    currentListId);
                currentListId = snapshot.ListEntityId;

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
