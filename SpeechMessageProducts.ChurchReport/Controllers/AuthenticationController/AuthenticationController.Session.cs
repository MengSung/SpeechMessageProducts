// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class AuthenticationController
// 主要成員：Logout、CheckSession、ExtendSession
// 引用命名空間：Microsoft.AspNetCore.Mvc、System
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Threading.Tasks;
using ChurchReport.Models;
using ChurchReport.Services.Caching;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（Session / 登出）
    /// </summary>
    public partial class AuthenticationController
    {
        #region 登出

        [HttpGet]
        [HttpPost]
        [Route("/Authentication/Logout")]
        [Route("/Logout")]
        public async Task<IActionResult> Logout()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("========================================");
                System.Diagnostics.Debug.WriteLine("[Logout] 開始登出流程");

                // ========================================
                // ? Session Fixation 防護 - 完全清除並銷毀 Session
                // ========================================
                // 不僅清除 Session 內容，還要確保 Session ID 被完全銷毀
                // 防止登出後 Session 被重用
                DrainCurrentDonationSessionResourcesAndClearSession();

                // 強制提交清除操作（確保立即生效）
                try
                {
                    await HttpContext.Session.CommitAsync();
                    System.Diagnostics.Debug.WriteLine("[Logout] ? Session 已清除並提交");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Logout] ?? Session Commit 警告: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("[Logout] ? 登出完成");
                System.Diagnostics.Debug.WriteLine("========================================");

                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                Response.Cookies.Delete(".ChurchReport.Session");
                Response.Cookies.Delete(".ChurchReport.Auth");

                return RedirectToAction("Login");
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[Logout] ? 登出失敗: {e.Message}");
                return HandleError(e, "Logout");
            }
        }

        #endregion

        /// <summary>
        /// 從目前 request 的主 DI container 解析 DonationPaymentManager coordinator，並執行身份重設前的唯一 drain/clear 順序。
        /// </summary>
        /// <remarks>
        /// 多數 legacy Controller 仍由 BaseChurchController 手動建立 InMemoryDataContext，因此此處不能依賴 scoped context Dispose。
        /// RequestServices 必須回傳 Startup 註冊的 singleton；遺漏時立即 fail closed，不建立 fallback owner，也不先清 Session。
        /// 方法不讀取、記錄或回傳 opaque scope、Session ID、使用者 ID、Token、Credential 或任何 CRM/LINE 資料。
        /// </remarks>
        private void DrainCurrentDonationSessionResourcesAndClearSession()
        {
            var coordinator = HttpContext.RequestServices.GetService(
                    typeof(SessionScopedResourceDisposalCoordinator<DonationPaymentManager>))
                as SessionScopedResourceDisposalCoordinator<DonationPaymentManager>
                ?? throw new InvalidOperationException(
                    "Session resource coordinator 尚未由 ChurchReport 主 DI 註冊；拒絕清除 Session 後遺留資源。");

            DrainDonationSessionResourcesAndClearSession(HttpContext.Session, coordinator);
        }

        /// <summary>
        /// 先撤銷舊 Donation generation 的新 request 可見性，再清除 Session 的唯一排序實作。
        /// </summary>
        /// <param name="session">尚未 Clear、仍可取得舊 opaque resource scope 的目前 Session。</param>
        /// <param name="coordinator">由主 DI singleton 唯一擁有的 DonationPaymentManager coordinator。</param>
        /// <remarks>
        /// <see cref="SessionScopedResourceDisposalCoordinator{TResource}.DrainSessionResourceScope"/> 只阻止新 lease；
        /// 已在執行的 request 可繼續使用 Manager，最後 lease 歸還後才 Dispose LINE client 與 Semaphore。
        /// 若 scope 受污染或 coordinator 已停止，例外會在 <see cref="ISession.Clear"/> 前傳出，確保身份重設 fail closed。
        /// </remarks>
        private static void DrainDonationSessionResourcesAndClearSession(
            ISession session,
            SessionScopedResourceDisposalCoordinator<DonationPaymentManager> coordinator)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(coordinator);

            coordinator.DrainSessionResourceScope(session);
            session.Clear();
        }

        #region Session 管理

        [HttpGet]
        [Route("/Authentication/CheckSession")]
        public IActionResult CheckSession()
        {
            try
            {
                bool isValid = InMemoryContext.ListManager.m_Account != null &&
                              InMemoryContext.ListManager.m_Account != "";

                return Json(new
                {
                    isValid,
                    userName = InMemoryContext.ListManager.LoginFullName ?? ""
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "CheckSession");
            }
        }

        [HttpPost]
        [Route("/Authentication/ExtendSession")]
        public IActionResult ExtendSession()
        {
            try
            {
                return Json(new { status = "1", message = "Session 已延長" });
            }
            catch (Exception e)
            {
                return HandleError(e, "ExtendSession");
            }
        }

        #endregion
    }
}
