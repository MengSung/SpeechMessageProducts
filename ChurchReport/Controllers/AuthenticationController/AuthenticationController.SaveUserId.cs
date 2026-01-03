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

                IOrganizationService service = null;
                try
                {
                    service = GetConnection();

                    var existingBindingResult = await CheckExistingLineBinding(service, UserLineId);
                    if (existingBindingResult != null)
                        return existingBindingResult;

                    return Json(new { status = "1", message = "請完成身分綁定註冊" });
                }
                finally
                {
                    ReleaseConnection(service);
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveUserId");
            }
        }

        #endregion
    }
}
