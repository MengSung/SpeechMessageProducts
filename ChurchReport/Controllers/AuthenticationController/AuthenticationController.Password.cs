using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（密碼管理：預留功能）
    /// </summary>
    public partial class AuthenticationController
    {
        #region 密碼管理 (預留功能)

        [HttpGet]
        [Route("/Authentication/ForgotPassword")]
        public IActionResult ForgotPassword()
        {
            try
            {
                return View();
            }
            catch (Exception e)
            {
                return HandleError(e, "ForgotPassword");
            }
        }

        [HttpPost]
        [Route("/Authentication/ResetPassword")]
        public async Task<IActionResult> ResetPassword(string email)
        {
            try
            {
                return Json(new { status = "1", message = "密碼重設郵件已發送" });
            }
            catch (Exception e)
            {
                return HandleError(e, "ResetPassword");
            }
        }

        [HttpPost]
        [Route("/Authentication/ChangePassword")]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword)
        {
            try
            {
                return Json(new { status = "1", message = "密碼已成功變更" });
            }
            catch (Exception e)
            {
                return HandleError(e, "ChangePassword");
            }
        }

        #endregion
    }
}
