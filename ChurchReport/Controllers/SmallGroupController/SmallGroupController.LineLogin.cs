using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - LINE 登入處理
    /// </summary>
    public partial class SmallGroupController
    {
        #region LINE 登入

        /// <summary>
        /// 處理 LINE 登入（非同步模式）
        /// </summary>
        private async Task<IActionResult> HandleLineLogin(
            string lineUserId, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var contactTask = Task.Run(() => 
                    ToolUtility.RetrieveContactEntityByLineUserId(lineUserId),
                    cancellationToken);

                var contact = await contactTask.ConfigureAwait(false);

                if (contact == null)
                {
                    return BadRequest("找不到對應的連絡人");
                }

                string fullName = contact.Attributes["fullname"].ToString();

                if (fullName.EndsWith("(Line)"))
                {
                    await _lineBindingNotificationService
                        .NotifyLineBindingAsync(lineUserId, cancellationToken)
                        .ConfigureAwait(false);
                    
                    return RedirectToAction("Login", "Authentication");
                }
                else
                {
                    var setupDataTask = Task.Run(() => 
                        InMemoryContext.SetupSmallGroupData(
                            fullName, "LineIdLogin", lineUserId, DateTime.Now, true),
                        cancellationToken);
                    
                    var setupViewBagTask = Task.Run(() => 
                        SetupViewBagForSmallGroup(), 
                        cancellationToken);
                    
                    var ensureDataTask = Task.Run(() => 
                        EnsureIntegrateDataLoaded(lineUserId),
                        cancellationToken);
                    
                    await Task.WhenAll(setupDataTask, setupViewBagTask, ensureDataTask)
                        .ConfigureAwait(false);

                    return View("~/Views/Home/IntegrateView.cshtml", 
                        InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
                }
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception e)
            {
                return HandleError(e, "HandleLineLogin");
            }
        }

        #endregion
    }
}
