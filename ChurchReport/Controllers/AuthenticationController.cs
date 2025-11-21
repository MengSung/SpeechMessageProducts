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
                // 記錄登入開始
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 開始處理登入 - 帳號: {aGalleryViewModel?.Account}, 時間: {DateTime.Now}");

                // 步驟 1: 驗證使用者身份
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 1: 驗證使用者身份");
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

                // 步驟 2: 取得使用者資料
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 2: 取得使用者資料");
                var (loginContact, fullName) = await RetrieveUserData(contactIdString, aGalleryViewModel);
                
                if (loginContact == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 無法取得使用者資料");
                    return Json(new
                    {
                        DisplayViewType = "登入錯誤",
                        ActiveListId = "",
                        message = "無法取得使用者資料",
                        fullname = ""
                    });
                }

                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 使用者: {fullName}");

                // 步驟 3: 初始化使用者 Session
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 3: 初始化使用者 Session");
                InitializeUserSession(loginContact, aGalleryViewModel);

                // 步驟 4: 設定系統資料
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 4: 設定系統資料 - 開始時間: {DateTime.Now}");
                SetupSystemData(loginContact, aGalleryViewModel);
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 4: 設定系統資料 - 完成時間: {DateTime.Now}");

                // 步驟 5: 判斷顯示視圖類型
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 5: 判斷顯示視圖類型");
                string displayViewType = DetermineDisplayViewType();
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 顯示類型: {displayViewType}");

                // 步驟 6: 設定 ViewBag 參數
                System.Diagnostics.Debug.WriteLine($"[ProcessLogin] 步驟 6: 設定 ViewBag 參數");
                SetupViewBagParameters(displayViewType);

                // 步驟 7: 返回登入結果
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
        /// ?x?s LINE ???? ID ???i?????n?J
        /// ?? LIFF ?e???U LINE ??????A?M?J?}?n?J?y?{
        /// </summary>
        /// <param name="UserLineId">LINE ???? ID</param>
        /// <param name="GroupId">?s??</param>
        /// <param name="RoomId">??????</param>
        /// <param name="ViewType">?????????</param>
        [HttpPost]
        [Route("/Authentication/SaveUserLineId")]
        public async Task<IActionResult> SaveUserLineId(string UserLineId, string GroupId, string RoomId, string ViewType)
        {
            try
            {
                // ?]?w LINE ?????????T
                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = RoomId;
                InMemoryContext.LineBindingViewModel.GroupId = GroupId;
                InMemoryContext.LineBindingViewModel.ViewType = ViewType;

                // ?]?w???? ID
                if (!string.IsNullOrEmpty(GroupId))
                    InMemoryContext.LineBindingViewModel.DisplayId = GroupId;
                else if (!string.IsNullOrEmpty(RoomId))
                    InMemoryContext.LineBindingViewModel.DisplayId = RoomId;
                else
                    InMemoryContext.LineBindingViewModel.DisplayId = UserLineId;

                // ??d????O?_?w?N??
                var loginContact = ToolUtility.RetrieveContactByLineId(UserLineId);
                
                if (loginContact == null)
                {
                    // ??????|?w?N??
                    return Json(new
                    {
                        DisplayViewType = "???N??",
                        ActiveListId = "",
                        message = "???N??",
                        fullname = ""
                    });
                }

                // ??? LINE ?n?J?? ViewModel
                var lineLoginViewModel = new GalleryViewModel
                {
                    Account = "",  // LINE ?n?J????n?b??
                    Password = UserLineId
                };

                // ?]?w LINE ?n?J??O
                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;

                // ??βΤ@???n?J?B?z?y?{
                return await ProcessLogin(lineLoginViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveUserLineId");
            }
        }

        /// <summary>
        /// ?B?z LINE ?n?J
        /// ?z?L LINE User ID ?i????????
        /// </summary>
        [HttpPost]
        [Route("/Authentication/ProcessLineLogin")]
        public async Task<IActionResult> ProcessLineLogin()
        {
            try
            {
                // ??? LINE ?n?J?? GalleryViewModel
                var lineLoginViewModel = new GalleryViewModel
                {
                    Account = "",  // LINE ?n?J????n?b??
                    Password = InMemoryContext.LineBindingViewModel.LineUserId
                };

                // ??βΤ@???n?J?B?z?y?{
                return await ProcessLogin(lineLoginViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "ProcessLineLogin");
            }
        }

        #endregion

        #region LINE 身分綁定註冊

        /// <summary>
        /// LINE LIFF 身分綁定註冊頁面
        /// 用於新用戶透過 LINE 註冊並綁定帳號
        /// </summary>
        /// <param name="LineIdLoginViewPatameter">LINE LIFF ID 參數</param>
        [HttpGet]
        [Route("/Authentication/LineLiffView/{LineIdLoginViewPatameter?}")]
        [Route("/LineLiffView/{LineIdLoginViewPatameter?}")]
        public IActionResult LineLiffView(string LineIdLoginViewPatameter)
        {
            try
            {
                // 若缺少必要參數，提供友善提示
                if (string.IsNullOrWhiteSpace(LineIdLoginViewPatameter))
                {
                    return RedirectToAction("DisplayErrorView", "Home", new { ErrorMessage = "缺少 LIFF 參數，請從 LINE 入口開啟。" });
                }

                var images = new List<string>
                {
                    Url.Content("~/assets/images/sunnyvalech.jpg")
                };

                InMemoryContext.LineBindingViewModel.Images = images;
                TempData["Proponent"] = LineIdLoginViewPatameter;

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, "LineLiffView");
            }
        }

        /// <summary>
        /// 處理 LINE 身分綁定註冊
        /// 建立新用戶並綁定 LINE ID
        /// </summary>
        /// <param name="model">LINE 綁定資料模型</param>
        [HttpPost]
        [Route("/Authentication/ProcessLineBinding")]
        public async Task<IActionResult> ProcessLineBinding(LineBindingViewModel model)
        {
            try
            {
                // 驗證必填欄位
                if (string.IsNullOrWhiteSpace(model.FullName))
                {
                    return Json(new { status = "0", message = "主要姓名必填" });
                }

                if (string.IsNullOrWhiteSpace(model.Mobile))
                {
                    return Json(new { status = "0", message = "行動電話必填" });
                }

                if (string.IsNullOrWhiteSpace(model.LineUserId))
                {
                    return Json(new { status = "0", message = "LINE User ID 遺失" });
                }

                // 檢查 LINE ID 是否已綁定
                var existingContact = ToolUtility.RetrieveContactByLineId(model.LineUserId);
                if (existingContact != null)
                {
                    return Json(new { 
                        status = "0", 
                        message = $"此 LINE 帳號已綁定至 {ToolUtility.GetEntityStringAttribute(existingContact, "fullname")}" 
                    });
                }

                // 檢查姓名是否已存在
                var contactsByName = ToolUtility.RetrieveContactCollectionByName(model.FullName);
                Entity targetContact = null;

                if (contactsByName != null && contactsByName.Entities.Count > 0)
                {
                    // 姓名已存在，嘗試匹配手機號碼
                    foreach (var contact in contactsByName.Entities)
                    {
                        var mobilePhone = ToolUtility.GetEntityStringAttribute(contact, "mobilephone");
                        if (mobilePhone == model.Mobile)
                        {
                            targetContact = contact;
                            break;
                        }
                    }

                    if (targetContact != null)
                    {
                        // 找到匹配的聯絡人，綁定 LINE ID
                        ToolUtility.SetEntityStringAttribute(ref targetContact, "new_lineuserid", model.LineUserId);
                        
                        // 更新其他資訊
                        if (!string.IsNullOrWhiteSpace(model.OtherName))
                        {
                            ToolUtility.SetEntityStringAttribute(ref targetContact, "lastname", model.OtherName);
                        }
                        
                        ToolUtility.UpdateEntity(ref targetContact);

                        return Json(new { 
                            status = "1", 
                            message = $"已成功綁定 LINE 至現有帳號：{model.FullName}" 
                        });
                    }
                }

                // 建立新聯絡人
                var newContact = new Entity("contact");
                ToolUtility.SetEntityStringAttribute(ref newContact, "fullname", model.FullName);
                ToolUtility.SetEntityStringAttribute(ref newContact, "mobilephone", model.Mobile);
                ToolUtility.SetEntityStringAttribute(ref newContact, "new_lineuserid", model.LineUserId);
                
                if (!string.IsNullOrWhiteSpace(model.OtherName))
                {
                    ToolUtility.SetEntityStringAttribute(ref newContact, "lastname", model.OtherName);
                }

                // 建立聯絡人
                var newContactId = ToolUtility.CreateEntity(newContact);

                if (newContactId != Guid.Empty)
                {
                    return Json(new { 
                        status = "1", 
                        message = $"註冊成功！歡迎 {model.FullName} 加入聖谷行道會" 
                    });
                }
                else
                {
                    return Json(new { 
                        status = "0", 
                        message = "註冊失敗，請稍後再試" 
                    });
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "ProcessLineBinding");
            }
        }

        /// <summary>
        /// 儲存 LINE 使用者 ID（用於身分綁定頁面）
        /// </summary>
        /// <param name="UserLineId">LINE 使用者 ID</param>
        /// <param name="GroupId">群組 ID</param>
        /// <param name="RoomId">聊天室 ID</param>
        /// <param name="ViewType">視圖類型</param>
        /// <param name="DisplayName">顯示名稱</param>
        /// <param name="PictureUrl">頭像 URL</param>
        /// <param name="StatusMessage">狀態訊息</param>
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
                // 設定 LINE 相關資訊
                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = RoomId ?? "";
                InMemoryContext.LineBindingViewModel.GroupId = GroupId ?? "";
                InMemoryContext.LineBindingViewModel.ViewType = ViewType ?? "";

                // 儲存額外資訊
                if (!string.IsNullOrEmpty(DisplayName))
                {
                    InMemoryContext.LineBindingViewModel.FullName = DisplayName;
                }

                // 檢查用戶是否已綁定
                var loginContact = ToolUtility.RetrieveContactByLineId(UserLineId);
                
                if (loginContact == null)
                {
                    // 用戶尚未綁定
                    return Json(new
                    {
                        status = "1",
                        message = "請完成身分綁定註冊"
                    });
                }
                else
                {
                    // 用戶已綁定
                    var fullName = ToolUtility.GetEntityStringAttribute(loginContact, "fullname");
                    return Json(new
                    {
                        status = "0",
                        message = $"您已綁定為 {fullName}"
                    });
                }
            }
            catch (Exception e)
            {
                return HandleError(e, "SaveUserId");
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
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 開始驗證 - 帳號: {viewModel?.Account}");
                
                string contactIdString = "";

                if (viewModel.Account != "")
                {
                    // 透過帳號密碼登入
                    System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 使用帳號密碼登入");
                    
                    // 檢查 ToolUtility 是否已初始化
                    if (ToolUtility == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] ToolUtility 未初始化");
                        return (false, "", "系統初始化錯誤，請重新登入");
                    }
                    
                    contactIdString = ToolUtility.RetrieveContactByAccountNumber(viewModel.Account, viewModel.Password);
                    System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 驗證結果: {contactIdString}");
                }
                else
                {
                    // 透過 Line Id 登入
                    System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 使用 LINE ID 登入");
                    contactIdString = "透過Line Id 登入";
                }

                // 檢查驗證結果
                if (contactIdString == "密碼錯誤" || 
                    contactIdString == "系統沒有設定密碼" || 
                    contactIdString == "帳號錯誤")
                {
                    System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 驗證失敗: {contactIdString}");
                    return (false, "", contactIdString);
                }

                System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 驗證成功");
                return (true, contactIdString, "");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 發生例外: {ex.Message}");
                return (false, "", $"驗證過程發生錯誤: {ex.Message}");
            }
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
            try
            {
                // 設定多個組長處理需要的資料
                System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 呼叫 SetupListManager - 開始時間: {DateTime.Now:HH:mm:ss.fff}");
                try
                {
                    InMemoryContext.ListManager.SetupListManager(
                        viewModel.Account, 
                        viewModel.Password, 
                        DateTime.Now);
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] SetupListManager 完成 - 時間: {DateTime.Now:HH:mm:ss.fff}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] SetupListManager 失敗: {ex.Message}");
                    throw new Exception($"設定小組資料失敗: {ex.Message}", ex);
                }

                // 差勤簽核 OR 場地及資源預約
                System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 呼叫 SetupAppointmentList - 開始時間: {DateTime.Now:HH:mm:ss.fff}");
                try
                {
                    InMemoryContext.AppointmentsListManager.SetupAppointmentList();
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] SetupAppointmentList 完成 - 時間: {DateTime.Now:HH:mm:ss.fff}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] SetupAppointmentList 失敗: {ex.Message}");
                    // 這個失敗不影響主要登入流程，記錄錯誤但繼續
                }

                // 設定奉獻金流
                System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 設定奉獻金流 - 開始時間: {DateTime.Now:HH:mm:ss.fff}");
                try
                {
                    if (loginContact != null)
                    {
                        InMemoryContext.QpayManager.LoginType = "網頁登入";
                        InMemoryContext.QpayManager.SetQpayModel(loginContact);
                    }
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 設定奉獻金流完成 - 時間: {DateTime.Now:HH:mm:ss.fff}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 設定奉獻金流失敗: {ex.Message}");
                    // 這個失敗不影響主要登入流程
                }

                // 設定需要點名的課程清單
                System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 設定課程清單 - 開始時間: {DateTime.Now:HH:mm:ss.fff}");
                try
                {
                    InMemoryContext.FeeList.SetupLessonList(viewModel.Account, viewModel.Password);
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 設定課程清單完成 - 時間: {DateTime.Now:HH:mm:ss.fff}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 設定課程清單失敗: {ex.Message}");
                    // 這個失敗不影響主要登入流程
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 整體失敗: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
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
