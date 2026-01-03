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
