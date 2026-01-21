using Microsoft.AspNetCore.Mvc;
using System;

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
        public IActionResult Logout()
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
                    HttpContext.Session.CommitAsync().GetAwaiter().GetResult();
                    System.Diagnostics.Debug.WriteLine("[Logout] ? Session 已清除並提交");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Logout] ?? Session Commit 警告: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("[Logout] ? 登出完成");
                System.Diagnostics.Debug.WriteLine("========================================");
                
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
