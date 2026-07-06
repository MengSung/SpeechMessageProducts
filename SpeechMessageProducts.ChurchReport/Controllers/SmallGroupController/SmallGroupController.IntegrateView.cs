// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.IntegrateView.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupController
// 主要成員：IntegrateView、SetupIntegrateViewData、ShouldLoadIntegrateData、HandleIntegrateViewLogin、EnsureIntegrateDataLoaded
// 引用命名空間：Microsoft.AspNetCore.Mvc、System、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - 整合式小組長點名功能
    /// </summary>
    public partial class SmallGroupController
    {
        #region 整合式小組長點名

        /// <summary>
        /// 整合式小組回報頁面
        /// 提供單一小組的詳細回報功能(點名、禱告、統計)
        /// </summary>
        [Route("/SmallGroup/IntegrateView/{LoginParameter}")]
        public async Task<IActionResult> IntegrateView(
            string LoginParameter,
            CancellationToken cancellationToken = default)
        {
            try
            {
                SetupIntegrateViewData(LoginParameter);
                SetupViewBagForSmallGroup();

                if (LoginParameter != "AccountPassword")
                {
                    return HandleIntegrateViewLogin(LoginParameter);
                }
                else if (LoginParameter == "jquery.js")
                {
                    ViewBag.LoginType = "個人回報";
                    return Ok();
                }
                else
                {
                    return await HandleLineLogin(LoginParameter, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception e)
            {
                return HandleError(e, "IntegrateView");
            }
        }

        /// <summary>
        /// 設定整合視圖資料
        /// </summary>
        private void SetupIntegrateViewData(string loginParameter)
        {
            bool shouldLoadData = ShouldLoadIntegrateData(loginParameter);

            if (shouldLoadData)
            {
                string listId = DetermineListId(loginParameter);
                InMemoryContext.ListManager.SetupIntegrateData(listId);
                InMemoryContext.ListManager.ActiveListId = listId;
            }

            ViewBag.ListId = InMemoryContext.ListManager.ActiveListId;
            ViewBag.SpiritualLeaderListId = InMemoryContext.ListManager.ActiveListId;
        }

        /// <summary>
        /// 判斷是否需要載入整合資料
        /// </summary>
        private bool ShouldLoadIntegrateData(string loginParameter)
        {
            var displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;

            if (displayViewType == "MultiGroupView")
            {
                return true;
            }

            return weeklyReport == null || !weeklyReport.LoadFlag;
        }

        /// <summary>
        /// 處理整合式頁面登入
        /// </summary>
        private IActionResult HandleIntegrateViewLogin(string loginParameter)
        {
            if (InMemoryContext.ListManager.LoginType == "個人回報")
            {
                return RedirectToAction("PersonalInfomationView", "Personal");
            }

            return View("~/Views/Home/IntegrateView.cshtml", InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
        }

        /// <summary>
        /// 確保整合資料已載入
        /// </summary>
        private void EnsureIntegrateDataLoaded(string id)
        {
            var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;

            if (weeklyReport == null || !weeklyReport.LoadFlag)
            {
                InMemoryContext.ListManager.SetupIntegrateData(id);
            }
        }

        #endregion
    }
}
