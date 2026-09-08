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
        /// 處理 LINE 登入，依序完成 LINE 身分解析、Authentication ticket 建立、
        /// Session 專屬小組資料初始化，以及整合週報快照載入。
        /// </summary>
        /// <param name="lineUserId">
        /// LINE 平台提供的使用者識別碼。此值只用於目前登入者的 Contact 查詢與 Session 身分建立，
        /// 絕對不可直接當成 Dataverse 小組 ListEntityId；小組 ID 必須由完成授權後的 ListManager 取得。
        /// </param>
        /// <param name="cancellationToken">
        /// 目前 HTTP request 的取消訊號。取消只會阻止尚未開始的下一個步驟；既有同步 Dataverse SDK
        /// 呼叫無法被安全中斷，因此不會把同步 I/O 包進無法真正取消的 Task.Run，也不會在 request
        /// 結束後留下捕獲 HttpContext、Session 或 credential 的背景工作。
        /// </param>
        /// <returns>
        /// 綁定完成時回傳整合頁面；尚未完成 LINE 綁定時導向登入頁；找不到 Contact 時 fail closed。
        /// </returns>
        /// <remarks>
        /// 此方法刻意採用嚴格的相依順序。舊版以 Task.WhenAll 同時修改同一個 InMemoryContext，
        /// 可能讓 ViewBag、ActiveListId 與週報資料分屬不同世代，甚至讓兩個 loader 同時 append
        /// 同一 Session 的 Members。現在每一步只在前一步成功後執行；任何例外都由外層錯誤處理
        /// 中止回應，未完成候選不會由 ListManager 發布。這個順序是防止跨使用者資料串用與重複列的
        /// 安全不變條件，不可為了表面平行化再次改回 Task.Run/Task.WhenAll。
        /// </remarks>
        private async Task<IActionResult> HandleLineLogin(
            string lineUserId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 在進入不可安全中斷的同步 CRM 呼叫前先尊重 request cancellation。
                // ToolUtility 的 Dataverse API 是同步介面；用 Task.Run 包裝只會佔用另一個 ThreadPool
                // 執行緒，client 取消後 CRM 呼叫仍會繼續，並讓 closure 延長保存 LINE 身分與 request
                // 狀態。直接呼叫可確保工作仍由目前 request 擁有，回傳或例外後即結束生命週期。
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

                    // 先以已驗證 Contact 建立此 Session 專屬的小組資料，再讀取 ViewBag 與 ActiveListId。
                    // 三個動作都會接觸同一個可變 InMemoryContext，必須維持先寫後讀的順序；若平行執行，
                    // 後兩者可能讀到前一位登入者、舊日期或尚未完成的 ListManager，形成 session leakage。
                    InMemoryContext.SetupSmallGroupData(
                        fullName, "LineIdLogin", lineUserId, DateTime.Now, true);
                    SetupViewBagForSmallGroup();

                    // LINE user id 只是登入身分，並不是小組 GUID。完成 SetupSmallGroupData 後，
                    // ActiveListId 才是由目前登入者可見名單推導出的授權小組；只允許使用這個 server-side
                    // 值載入資料，避免 caller-provided identity 被誤當授權路由，也避免 new Guid(lineUserId)
                    // 造成格式錯誤。ListManager 會在自己的發布 gate 內再次驗證此 ID 確實可見。
                    EnsureIntegrateDataLoaded(InMemoryContext.ListManager.ActiveListId);

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
