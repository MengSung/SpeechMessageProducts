/*
 * 檔案整體說明
 *
 * 目的與定位：認證控制器（SaveUserId：儲存 LINE 使用者資訊並判斷是否已綁定）
 *
 * 主要功能與流程：
 * 本檔宣告 AuthenticationController。
 * 主要操作入口包含 SaveUserId。
 *
 * 使用邊界與維護：
 * 本檔與同名控制器的其他partial共用一次請求的依賴及狀態；授權、Session與資料範圍必須沿既有伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 * 可沿 ChurchReport.MemberInfo.Tests/Security/LineLoginOAuthSensitiveLoggingTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Core.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineBinding.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLogin.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOA
 * uth.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（SaveUserId：儲存 LINE 使用者資訊並判斷是否已綁定）
    /// </summary>
    public partial class AuthenticationController
    {
        #region LINE ID 儲存

        [HttpPost]
        [Route("/Authentication/SaveUserId")]
        public async Task<IActionResult> SaveUserId(
            string UserLineId,
            string GroupId,
            string RoomId,
            string ViewType,
            string DisplayName = "",
            string PictureUrl = "",
            string StatusMessage = "")
        {
            try
            {
                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = RoomId ?? "";
                InMemoryContext.LineBindingViewModel.GroupId = GroupId ?? "";
                InMemoryContext.LineBindingViewModel.ViewType = ViewType ?? "";

                if (!string.IsNullOrEmpty(DisplayName))
                {
                    InMemoryContext.LineBindingViewModel.FullName = DisplayName;
                }

                var service = _organizationService;
                var existingBindingResult = await CheckExistingLineBinding(service, UserLineId);
                if (existingBindingResult != null)
                    return existingBindingResult;

                return Json(new { status = "1", message = "請完成身分綁定註冊" });
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveUserId");
            }
        }

        #endregion
    }
}
