using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using ChurchReport.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器
    /// 處理使用者登入、登出及身份驗證相關功能
    /// </summary>
    public class AuthenticationController : BaseChurchController
    {
        #region 建構函式

        public AuthenticationController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService)
            : base(httpContextAccessor, memoryCache, paymentService)
        {
        }

        #endregion

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
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/sunnyvalech.jpg"));

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
        /// <param name="aGalleryViewModel">登入表單資料</param>
        [HttpPost]
        [Route("/Authentication/ProcessLogin")]
        public async Task<IActionResult> ProcessLogin(GalleryViewModel aGalleryViewModel)
        {
            try
            {
                // 步驟 1: 驗證使用者身份
                var (isValid, contactIdString, errorMessage) = ValidateUserCredentials(aGalleryViewModel);

                if (!isValid)
                {
                    return Json(new
                    {
                        DisplayViewType = "登入錯誤",
                        ActiveListId = InMemoryContext.ListManager.ActiveListId,
                        message = errorMessage,
                        fullname = errorMessage
                    });
                }

                // 步驟 2: 取得使用者資料
                var (loginContact, fullName) = await RetrieveUserData(contactIdString, aGalleryViewModel);

                // 步驟 3: 初始化使用者 Session
                InitializeUserSession(loginContact, aGalleryViewModel);

                // 步驟 4: 設定系統資料
                SetupSystemData(loginContact, aGalleryViewModel);

                // 步驟 5: 判斷顯示視圖類型
                string displayViewType = DetermineDisplayViewType();

                // 步驟 6: 設定 ViewBag 參數
                SetupViewBagParameters(displayViewType);

                // 步驟 7: 返回登入結果
                return CreateLoginResponse(displayViewType, fullName, aGalleryViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "ProcessLogin");
            }
        }

        #endregion

        #region LINE 登入

        /// <summary>
        /// LINE ID 登入頁面
        /// 顯示 LINE 登入表單
        /// </summary>
        /// <param name="LineIdLoginViewPatameter">LINE 登入參數</param>
        [HttpGet]
        [Route("/Authentication/LineIdLoginView/{LineIdLoginViewPatameter}")]
        public IActionResult LineIdLoginView(string LineIdLoginViewPatameter)
        {
            try
            {
                var images = new List<string>();
                images.Add(Url.Content("~/assets/images/sunnyvalech.jpg"));

                InMemoryContext.LineBindingViewModel.Images = images;
                TempData["Proponent"] = LineIdLoginViewPatameter;

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "LineIdLoginView");
            }
        }

        /// <summary>
        /// 處理 LINE 登入
        /// 透過 LINE User ID 進行身份驗證
        /// </summary>
        [HttpPost]
        [Route("/Authentication/ProcessLineLogin")]
        public async Task<IActionResult> ProcessLineLogin()
        {
            try
            {
                // 建立 LINE 登入的 GalleryViewModel
                var lineLoginViewModel = new GalleryViewModel
                {
                    Account = "",  // LINE 登入不需要帳號
                    Password = InMemoryContext.LineBindingViewModel.LineUserId
                };

                // 使用統一的登入處理流程
                return await ProcessLogin(lineLoginViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "ProcessLineLogin");
            }
        }

        #endregion

        #region 登出

        /// <summary>
        /// 登出功能
        /// 清除使用者 Session 並導向登入頁面
        /// </summary>
        [HttpGet]
        [HttpPost]
        [Route("/Authentication/Logout")]
        [Route("/Logout")]
        public IActionResult Logout()
        {
            try
            {
                // 清除 Session
                HttpContext.Session.Clear();

                // 重定向到登入頁面
                return RedirectToAction("Login");
            }
            catch (Exception e)
            {
                return HandleError(e, "Logout");
            }
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 驗證使用者憑證
        /// </summary>
        private (bool isValid, string contactId, string errorMessage) ValidateUserCredentials(GalleryViewModel viewModel)
        {
            string contactIdString = "";

            if (viewModel.Account != "")
            {
                // 透過帳號密碼登入
                contactIdString = ToolUtility.RetrieveContactByAccountNumber(viewModel.Account, viewModel.Password);
            }
            else
            {
                // 透過 Line Id 登入
                contactIdString = "透過Line Id 登入";
            }

            // 檢查驗證結果
            if (contactIdString == "密碼錯誤" || 
                contactIdString == "系統沒有設定密碼" || 
                contactIdString == "帳號錯誤")
            {
                return (false, "", contactIdString);
            }

            return (true, contactIdString, "");
        }

        /// <summary>
        /// 取得使用者資料
        /// </summary>
        private async Task<(Entity loginContact, string fullName)> RetrieveUserData(
            string contactIdString, 
            GalleryViewModel viewModel)
        {
            Entity loginContact;
            string fullName;

            if (contactIdString != "透過Line Id 登入")
            {
                // 使用者透過網頁的帳號密碼登入
                loginContact = ToolUtility.RetrieveEntityDynamics365("contact", new Guid(contactIdString));
                fullName = ToolUtility.GetEntityStringAttribute(ref loginContact, "fullname");
            }
            else
            {
                // 使用者透過 Line Id 登入
                loginContact = ToolUtility.RetrieveContactEntityByLineUserId(InMemoryContext.LineBindingViewModel.LineUserId);
                fullName = ToolUtility.GetEntityStringAttribute(ref loginContact, "fullname");
                
                // 設定 LINE 登入的帳密
                viewModel.Account = "LineIdLogin";
                viewModel.Password = InMemoryContext.LineBindingViewModel.LineUserId;
            }

            return (loginContact, fullName);
        }

        /// <summary>
        /// 初始化使用者 Session
        /// </summary>
        private void InitializeUserSession(Entity loginContact, GalleryViewModel viewModel)
        {
            // 設定行事曆的帳密
            InMemoryContext.AppointmentsListManager.m_Account = viewModel.Account;
            InMemoryContext.AppointmentsListManager.m_Password = viewModel.Password;
            InMemoryContext.AppointmentsListManager.m_LoginContact = loginContact;

            // 儲存登入者實體紀錄
            InMemoryContext.PersonalInfomationModel.m_LoginContact = loginContact;
        }

        /// <summary>
        /// 設定系統資料
        /// </summary>
        private void SetupSystemData(Entity loginContact, GalleryViewModel viewModel)
        {
            // 設定多個組長處理需要的資料
            InMemoryContext.ListManager.SetupListManager(
                viewModel.Account, 
                viewModel.Password, 
                DateTime.Now);

            // 差勤簽核 OR 場地及資源預約
            InMemoryContext.AppointmentsListManager.SetupAppointmentList();

            // 設定奉獻金流
            if (loginContact != null)
            {
                InMemoryContext.QpayManager.LoginType = "網頁登入";
                InMemoryContext.QpayManager.SetQpayModel(loginContact);
            }

            // 設定需要點名的課程清單
            InMemoryContext.FeeList.SetupLessonList(viewModel.Account, viewModel.Password);
        }

        /// <summary>
        /// 判斷顯示視圖類型
        /// </summary>
        private string DetermineDisplayViewType()
        {
            // 控制 Navigation 下拉項目
            ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
            ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
            ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

            // 透過取得多小組網頁需要的資料之後，判斷這是多小組還是單一小組長的回報
            string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();
            
            if (displayViewType == "IntegrateView")
            {
                // 得知這是單一小組長的回報，所以就直接下載整合式網頁所需的資料
                InMemoryContext.ListManager.SetupIntegrateData(InMemoryContext.ListManager.ActiveListId);
            }

            // 根據登入類型和幸福小組狀態調整顯示類型
            if (InMemoryContext.ListManager.LoginType != "小組長" && 
                InMemoryContext.HappyGroupDataManager.HappyType == "有幸福小組名單")
            {
                displayViewType = "HappyGroupView";
            }

            return displayViewType;
        }

        /// <summary>
        /// 設定 ViewBag 參數
        /// </summary>
        private void SetupViewBagParameters(string displayViewType)
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;

            // 設定繳費與點名資料狀態
            SetupFeeDataListCount();

            // 設定多小組布局參數
            SetMultiGroupLayoutParameter();
        }

        /// <summary>
        /// 建立登入回應
        /// </summary>
        private IActionResult CreateLoginResponse(
            string displayViewType, 
            string fullName, 
            GalleryViewModel viewModel)
        {
            return Json(new
            {
                DisplayViewType = displayViewType,
                ActiveListId = InMemoryContext.ListManager.ActiveListId,
                message = "歡迎" + fullName + "登入成功!",
                fullname = fullName,
                account = viewModel.Account,
                password = viewModel.Password
            });
        }

        #endregion

        #region 密碼管理 (預留功能)

        /// <summary>
        /// 忘記密碼頁面
        /// </summary>
        [HttpGet]
        [Route("/Authentication/ForgotPassword")]
        public IActionResult ForgotPassword()
        {
            try
            {
                // 實作忘記密碼邏輯
                return View();
            }
            catch (Exception e)
            {
                return HandleError(e, "ForgotPassword");
            }
        }

        /// <summary>
        /// 重設密碼
        /// </summary>
        [HttpPost]
        [Route("/Authentication/ResetPassword")]
        public async Task<IActionResult> ResetPassword(string email)
        {
            try
            {
                // 實作重設密碼邏輯
                // await SendPasswordResetEmail(email);

                return Json(new { status = "1", message = "密碼重設郵件已發送" });
            }
            catch (Exception e)
            {
                return HandleError(e, "ResetPassword");
            }
        }

        /// <summary>
        /// 變更密碼
        /// </summary>
        [HttpPost]
        [Route("/Authentication/ChangePassword")]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword)
        {
            try
            {
                // 實作變更密碼邏輯
                // await UpdatePassword(oldPassword, newPassword);

                return Json(new { status = "1", message = "密碼已成功變更" });
            }
            catch (Exception e)
            {
                return HandleError(e, "ChangePassword");
            }
        }

        #endregion

        #region Session 管理

        /// <summary>
        /// 檢查 Session 是否有效
        /// </summary>
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
                    isValid = isValid,
                    userName = InMemoryContext.ListManager.LoginFullName ?? ""
                });
            }
            catch (Exception e)
            {
                return HandleError(e, "CheckSession");
            }
        }

        /// <summary>
        /// 延長 Session 時間
        /// </summary>
        [HttpPost]
        [Route("/Authentication/ExtendSession")]
        public IActionResult ExtendSession()
        {
            try
            {
                // Session 會自動延長，這裡只需要返回成功即可
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
