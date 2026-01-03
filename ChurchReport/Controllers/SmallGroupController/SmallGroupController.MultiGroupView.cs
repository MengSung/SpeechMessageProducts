using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - 多小組回報功能
    /// </summary>
    public partial class SmallGroupController
    {
        #region 多小組回報

        /// <summary>
        /// 多小組回報主頁面
        /// 顯示多個小組的統計資訊與管理功能
        /// </summary>
        /// <param name="LoginParameter">登入參數(AccountPassword 或 LineId)</param>
        /// <param name="cancellationToken">取消標記</param>
        [Route("/SmallGroup/MultiGroupView/{LoginParameter}")]
        public async Task<IActionResult> MultiGroupView(
            string LoginParameter,
            CancellationToken cancellationToken = default)
        {
            try
            {
                SetupViewBagForSmallGroup();
                ViewBag.ListId = InMemoryContext.ListManager.ActiveListId;

                if (LoginParameter != "AccountPassword")
                {
                    return HandleMultiGroupLogin(LoginParameter);
                }
                else if (LoginParameter == "jquery.js")
                {
                    ViewBag.LoginType = "個人登入";
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
                return HandleError(e, "MultiGroupView");
            }
        }

        /// <summary>
        /// 處理多小組登入邏輯
        /// </summary>
        private IActionResult HandleMultiGroupLogin(string loginParameter)
        {
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();

            if (displayViewType == "MultiGroupView")
            {
                // 清除整合頁面資料
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport != null)
                {
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.LoadFlag = false;
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport = null;
                }

                // 設定多組資料
                if (InMemoryContext.ListManager.InitialFlag)
                {
                    InMemoryContext.ListManager.SetupListManager(
                        InMemoryContext.ListManager.m_Account,
                        InMemoryContext.ListManager.m_Password,
                        InMemoryContext.ListManager.m_SelectDate);
                }
                else
                {
                    InMemoryContext.ListManager.InitialFlag = true;
                }

                return View("~/Views/Home/MultiGroupView.cshtml", InMemoryContext.ListManager);
            }
            else
            {
                return RedirectToAction("IntegrateView", "SmallGroup");
            }
        }

        #endregion
    }
}
