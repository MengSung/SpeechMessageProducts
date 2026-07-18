// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupController
// 主要成員：HandleLineLogin
// 引用命名空間：Microsoft.AspNetCore.Mvc、System、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
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
                cancellationToken.ThrowIfCancellationRequested();
                var contact = ToolUtility.RetrieveContactEntityByLineUserId(lineUserId);

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
                    HttpContext?.Session?.SetString("_LoginAccount", "LineIdLogin");
                    HttpContext?.Session?.SetString("_LoginPassword", lineUserId);
                    HttpContext?.Session?.SetString("_SessionUserId", lineUserId);
                    await IssueAuthTicketAsync(contact.Id.ToString(), "LineIdLogin", lineUserId, "LINE");

                    cancellationToken.ThrowIfCancellationRequested();
                    InMemoryContext.SetupSmallGroupData(
                        fullName, "LineIdLogin", lineUserId, DateTime.Now, true);
                    SetupViewBagForSmallGroup();
                    EnsureIntegrateDataLoaded(lineUserId);

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
