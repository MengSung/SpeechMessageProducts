/*
 * 檔案整體說明
 *
 * 目的與定位：提供一般帳號登入頁與ProcessLogin提交入口，將目前表單交既有認證及Session初始化流程，再回傳固定登入結果；表單內容只是候選，密碼不可進入公開JSON或claims，服務與結果限目前請求使用。
 *
 * 主要功能與流程：
 * 本檔宣告 AuthenticationController。
 * 主要操作入口包含 Login、ProcessLogin。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
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
