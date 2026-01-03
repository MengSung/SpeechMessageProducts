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
                HttpContext.Session.Clear();
                return RedirectToAction("Login");
            }
            catch (Exception e)
            {
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
