/*
 * 檔案整體說明
 *
 * 目的與定位：小組管理控制器 - LINE 登入處理
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupController。
 * - 處理 LINE 登入，依序完成 LINE 身分解析、Authentication ticket 建立、 Session 專屬小組資料初始化，以及整合週報快照載入。
 * 主要操作入口包含 HandleLineLogin。
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

                    // 登入初始化完成後再次取得同一授權 scope 的 detached read，讓 Razor 永遠
                    // 只持有 request-owned 物件，不會捕獲 Session holder、credential 或共享 Members。
                    var detachedRead = InMemoryContext.ListManager
                        .EnsureAndGetIntegrateDetachedRead(InMemoryContext.ListManager.ActiveListId);
                    return View("~/Views/Home/IntegrateView.cshtml", detachedRead);
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
