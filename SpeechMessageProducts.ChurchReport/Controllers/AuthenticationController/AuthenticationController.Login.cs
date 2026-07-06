// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/AuthenticationController/AuthenticationController.Login.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class AuthenticationController
// 主要成員：Login、ProcessLogin
// 引用命名空間：ChurchReport.ViewModel、Microsoft.AspNetCore.Mvc、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器（登入頁/登入流程入口）
    /// </summary>
    public partial class AuthenticationController
    {
        #region 登入頁面

        /// <summary>
        /// 登入頁面
        /// 顯示帳號密碼登入表單
        /// </summary>
        [HttpGet]
        [Route("/Authentication/Login")]
        [Route("/Login")]
        [Route("/")]
        public async Task<IActionResult> Login()
        {
            try
            {
                var images = BuildHeroImages(
                    "~/assets/images/church-001.jpg",
                    "~/assets/images/church-002.jpg"
                );

                return View(new GalleryViewModel
                {
                    Images = images
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "Login");
            }
        }

        #endregion

        #region 處理登入

        /// <summary>
        /// 處理登入請求
        /// 驗證帳號密碼並建立使用者 Session
        /// </summary>
        [HttpPost]
        [Route("/Authentication/ProcessLogin")]
        public async Task<IActionResult> ProcessLogin(GalleryViewModel aGalleryViewModel)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 開始處理登入 - 帳號: {aGalleryViewModel?.Account}, 時間: {DateTime.Now}");

                System.Diagnostics.Debug.WriteLine("[ProcessLogin] 步驟 1: 驗證使用者身份");
                var (isValid, contactIdString, errorMessage) = ValidateUserCredentials(aGalleryViewModel);

                if (!isValid)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 驗證失敗: {errorMessage}");
                    return Json(new
                    {
                        DisplayViewType = "登入錯誤",
                        ActiveListId = InMemoryContext?.ListManager?.ActiveListId ?? "",
                        message = errorMessage,
                        fullname = errorMessage
                    });
                }

                System.Diagnostics.Debug.WriteLine("[ProcessLogin] 步驟 2: 取得使用者資料");
                var (loginContact, fullName) = await RetrieveUserData(contactIdString, aGalleryViewModel);

                if (loginContact == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessLogin] 無法取得使用者資料");
                    return Json(new
                    {
                        DisplayViewType = "登入錯誤",
                        ActiveListId = "",
                        message = "無法取得使用者資料",
                        fullname = ""
                    });
                }

                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 使用者: {fullName}");

                System.Diagnostics.Debug.WriteLine("[ProcessLogin] 步驟 3: 初始化使用者 Session");
                await InitializeUserSessionAsync(loginContact, aGalleryViewModel);

                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 4: 設定系統資料 - 開始時間: {DateTime.Now}");
                SetupSystemData(loginContact, aGalleryViewModel);
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 4: 設定系統資料 - 完成時間: {DateTime.Now}");

                System.Diagnostics.Debug.WriteLine("[ProcessLogin] 步驟 5: 判斷顯示視圖類型");
                string displayViewType = DetermineDisplayViewType();
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 顯示類型: {displayViewType}");

                System.Diagnostics.Debug.WriteLine("[ProcessLogin] 步驟 6: 設定 ViewBag 參數");
                SetupViewBagParameters(displayViewType);

                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 7: 返回登入結果 - 完成時間: {DateTime.Now}");
                return CreateLoginResponse(displayViewType, fullName, aGalleryViewModel);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 發生錯誤: {e.Message}\n堆疊追蹤: {e.StackTrace}");
                return HandleError(e, "ProcessLogin");
            }
        }

        #endregion
    }
}
