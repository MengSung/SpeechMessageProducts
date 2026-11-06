/*
 * 檔案整體說明
 *
 * 目的與定位：小組管理控制器 - 整合式小組長點名功能
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupController。
 * - 整合式小組回報頁面 提供單一小組的詳細回報功能(點名、禱告、統計)
 * - 處理整合式頁面登入並建立 request-owned Razor model。伺服器重新驗證 ActiveListId， detached snapshot 在回應完成後可回收，不會把 Session graph 
 * 暴露給 Razor callback。
 * 主要操作入口包含 IntegrateView、SetupIntegrateViewData、ShouldLoadIntegrateData、HandleIntegrateViewLogin、
 * EnsureIntegrateDataLoaded。
 *
 * 使用邊界與維護：
 * 本檔與同名控制器的其他partial共用一次請求的依賴及狀態；授權、Session與資料範圍必須沿既有伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Cache.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Core.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Date.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
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
        /// 處理整合式頁面登入並建立 request-owned Razor model。伺服器重新驗證 ActiveListId，
        /// detached snapshot 在回應完成後可回收，不會把 Session graph 暴露給 Razor callback。
        /// </summary>
        private IActionResult HandleIntegrateViewLogin(string loginParameter)
        {
            if (InMemoryContext.ListManager.LoginType == "個人回報")
            {
                return RedirectToAction("PersonalInfomationView", "Personal");
            }

            // Razor 只接收目前授權 scope 的 detached snapshot；不可把 Session holder 內的
            // 可變週報物件圖直接交給 View，避免另一個 request 的 CRUD 或日期切換在 Razor
            // 列舉期間改寫同一份 Members，也避免跨世代資料被當成目前頁面的 fallback。
            var detachedRead = InMemoryContext.ListManager
                .EnsureAndGetIntegrateDetachedRead(InMemoryContext.ListManager.ActiveListId);
            return View("~/Views/Home/IntegrateView.cshtml", detachedRead);
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
