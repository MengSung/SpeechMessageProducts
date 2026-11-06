/*
 * 檔案整體說明
 *
 * 目的與定位：提供登出、Session狀態檢查與延長入口，協調目前登入ticket及Session有效性；檢查結果只屬當前使用者，延長請求不能建立其他使用者身分或復用其資料。
 *
 * 主要功能與流程：
 * 本檔宣告 AuthenticationController。
 * 主要操作入口包含 Logout、CheckSession、ExtendSession。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
                HttpContext.Session.Clear();

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
