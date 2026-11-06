/*
 * 檔案整體說明
 *
 * 目的與定位：接收LINE登入畫面提供的工作識別／顯示資料並檢查既有綁定，決定後續登入或綁定畫面；此入口保留舊前端契約，顯示資料不得取代伺服器認證，暫存資料限目前Session範圍。
 *
 * 主要功能與流程：
 * 本檔宣告 AuthenticationController。
 * 主要操作入口包含 SaveUserId。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
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
