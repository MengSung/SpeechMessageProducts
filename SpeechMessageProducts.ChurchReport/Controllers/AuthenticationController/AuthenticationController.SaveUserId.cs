// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/AuthenticationController/AuthenticationController.SaveUserId.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class AuthenticationController
// 主要成員：SaveUserId
// 引用命名空間：Microsoft.AspNetCore.Mvc、Microsoft.Xrm.Sdk、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
