// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.Date.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupController
// 主要成員：UpdateHappyWeekIndex、UpdateHappyWeekTopic、UpdateDate、UpdateIntegrateDate
// 引用命名空間：Microsoft.AspNetCore.Mvc、System、System.Globalization
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
