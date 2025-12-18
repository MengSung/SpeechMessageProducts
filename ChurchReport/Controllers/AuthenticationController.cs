using ChurchReport.Models;
using ChurchReport.Tools;
using ChurchReport.ViewModel;
using ChurchReport.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;
using System.Text.RegularExpressions;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 認證控制器
    /// 處理使用者登入、登出及身份驗證相關功能
    /// </summary>
    public class AuthenticationController : BaseChurchController
    {
        #region 建構函式

        /// <summary>
        /// AuthenticationController 建構函數 (使用 Dependency Injection)
        /// </summary>
        /// <param name="httpContextAccessor">HTTP 上下文存取器</param>
        /// <param name="memoryCache">記憶體快取</param>
        /// <param name="paymentService">金流服務</param>
        /// <param name="toolUtilityProvider">ToolUtility 提供者 (DI 注入)</param>
        /// <param name="connectionPool">CRM 連線池</param>
        public AuthenticationController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment paymentService,
            IToolUtilityProvider toolUtilityProvider,
            ICrmConnectionPool connectionPool)
        : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)
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
                images.Add(Url.Content("~/assets/images/church-001.jpg"));

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
                images.Add(Url.Content("~/assets/images/church-001.jpg"));

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
                // ===== 步驟 1: 記錄請求開始 =====
                System.Diagnostics.Debug.WriteLine($"========================================");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ===== 開始處理 LINE 登入請求 =====");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 請求參數:");
                System.Diagnostics.Debug.WriteLine($"  - UserLineId: {UserLineId}");
                System.Diagnostics.Debug.WriteLine($"  - GroupId: {GroupId ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"  - RoomId: {RoomId ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"  - ViewType: {ViewType ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"========================================");

                // ===== 步驟 2: 設定 LINE 相關資訊 =====
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 步驟 2: 設定 LINE 相關資訊到 InMemoryContext");

                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
                InMemoryContext.LineBindingViewModel.RoomId = RoomId;
                InMemoryContext.LineBindingViewModel.GroupId = GroupId;
                InMemoryContext.LineBindingViewModel.ViewType = ViewType;

                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] InMemoryContext 設定完成:");
                System.Diagnostics.Debug.WriteLine($"  - LineUserId: {InMemoryContext.LineBindingViewModel.LineUserId}");
                System.Diagnostics.Debug.WriteLine($"  - RoomId: {InMemoryContext.LineBindingViewModel.RoomId}");
                System.Diagnostics.Debug.WriteLine($"  - GroupId: {InMemoryContext.LineBindingViewModel.GroupId}");
                System.Diagnostics.Debug.WriteLine($"  - ViewType: {InMemoryContext.LineBindingViewModel.ViewType}");

                // ===== 步驟 3: 設定顯示 ID =====
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 步驟 3: 設定 DisplayId");

                if (!string.IsNullOrEmpty(GroupId))
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = GroupId;
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] DisplayId 設定為 GroupId: {GroupId}");
                }
                else if (!string.IsNullOrEmpty(RoomId))
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = RoomId;
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] DisplayId 設定為 RoomId: {RoomId}");
                }
                else
                {
                    InMemoryContext.LineBindingViewModel.DisplayId = UserLineId;
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] DisplayId 設定為 UserLineId: {UserLineId}");
                }

                // ===== 步驟 4: 檢查用戶是否已綁定 =====
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 步驟 4: 檢查用戶是否已在資料庫中綁定");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 準備從連接池獲取 CRM 連接...");

                IOrganizationService service = null;
                try
                {
                    var connectionStartTime = DateTime.Now;
                    service = GetConnection();
                    var connectionEndTime = DateTime.Now;
                    var connectionDuration = (connectionEndTime - connectionStartTime).TotalMilliseconds;

                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ✅ 成功從連接池獲取連接");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 獲取連接耗時: {connectionDuration:F2} ms");

                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 開始查詢資料庫，檢查 LINE ID: {UserLineId}");

                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet("contactid", "fullname"),
                        Criteria = new FilterExpression
                        {
                            FilterOperator = LogicalOperator.And,
                            Conditions =
                            {
                                new ConditionExpression("new_lineid", ConditionOperator.Equal, UserLineId),
                                new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                            }
                        },
                        TopCount = 1
                    };

                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] QueryExpression 設定:");
                    System.Diagnostics.Debug.WriteLine($"  - Entity: contact");
                    System.Diagnostics.Debug.WriteLine($"  - Columns: contactid, fullname");
                    System.Diagnostics.Debug.WriteLine($"  - Criteria: new_lineid = '{UserLineId}' AND statecode = 0");
                    System.Diagnostics.Debug.WriteLine($"  - TopCount: 1");

                    var queryStartTime = DateTime.Now;
                    var results = service.RetrieveMultiple(query);
                    var queryEndTime = DateTime.Now;
                    var queryDuration = (queryEndTime - queryStartTime).TotalMilliseconds;

                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ✅ 資料庫查詢完成");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 查詢耗時: {queryDuration:F2} ms");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 查詢結果數量: {results.Entities.Count}");

                    if (results.Entities.Count == 0)
                    {
                        // ===== 情況 A: 用戶尚未綁定 =====
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ❌ 用戶尚未綁定");
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 準備返回「尚未綁定」回應");
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 回應內容:");
                        System.Diagnostics.Debug.WriteLine($"  - DisplayViewType: '尚未綁定'");
                        System.Diagnostics.Debug.WriteLine($"  - ActiveListId: ''");
                        System.Diagnostics.Debug.WriteLine($"  - message: '尚未綁定'");
                        System.Diagnostics.Debug.WriteLine($"  - fullname: ''");
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ===== 處理結束 (尚未綁定) =====");
                        System.Diagnostics.Debug.WriteLine($"========================================\n");

                        return Json(new
                        {
                            DisplayViewType = "尚未綁定",
                            ActiveListId = "",
                            message = "尚未綁定",
                            fullname = ""
                        });
                    }

                    // ===== 情況 B: 用戶已綁定 =====
                    var contactId = results.Entities[0].Id;
                    var fullName = results.Entities[0].Contains("fullname")
                        ? results.Entities[0].GetAttributeValue<string>("fullname")
                        : "";

                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ✅ 用戶已綁定，找到匹配的聯絡人");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 聯絡人資訊:");
                    System.Diagnostics.Debug.WriteLine($"  - ContactId: {contactId}");
                    System.Diagnostics.Debug.WriteLine($"  - FullName: {fullName}");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 準備進入登入流程");
                }
                catch (FaultException<OrganizationServiceFault> ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ❌ CRM 服務異常");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 異常類型: FaultException<OrganizationServiceFault>");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 異常訊息: {ex.Detail?.Message ?? ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 堆疊追蹤: {ex.StackTrace}");
                    throw;
                }
                catch (TimeoutException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ❌ 連接超時");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 異常類型: TimeoutException");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 異常訊息: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 堆疊追蹤: {ex.StackTrace}");
                    throw;
                }
                finally
                {
                    if (service != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 歸還 CRM 連接到連接池");
                        ReleaseConnection(service);
                        System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ✅ 連接已歸還");
                    }
                }

                // ===== 步驟 5: 建立 LINE 登入的 ViewModel =====
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 步驟 5: 建立 LINE 登入的 ViewModel");

                var lineLoginViewModel = new GalleryViewModel
                {
                    Account = "",  // LINE 登入不需要帳號
                    Password = UserLineId
                };

                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] GalleryViewModel 建立完成:");
                System.Diagnostics.Debug.WriteLine($"  - Account: '{lineLoginViewModel.Account}' (空字串)");
                System.Diagnostics.Debug.WriteLine($"  - Password: {lineLoginViewModel.Password}");

                // ===== 步驟 6: 重新設定 LINE 登入標記 (確保) =====
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 步驟 6: 重新設定 LINE 登入標記");
                InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] LineUserId 已重新設定: {UserLineId}");

                // ===== 步驟 7: 呼叫統一的登入處理流程 =====
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 步驟 7: 準備呼叫 ProcessLogin 方法");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ===== 轉向 ProcessLogin 處理登入 =====");
                System.Diagnostics.Debug.WriteLine($"========================================\n");

                return await ProcessLogin(lineLoginViewModel);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"========================================");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ❌❌❌ 發生未預期的錯誤 ❌❌❌");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 異常類型: {e.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 異常訊息: {e.Message}");
                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 堆疊追蹤:");
                System.Diagnostics.Debug.WriteLine(e.StackTrace);

                if (e.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 內部異常類型: {e.InnerException.GetType().Name}");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 內部異常訊息: {e.InnerException.Message}");
                    System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 內部異常堆疊追蹤:");
                    System.Diagnostics.Debug.WriteLine(e.InnerException.StackTrace);
                }

                System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] ===== 處理結束 (異常) =====");
                System.Diagnostics.Debug.WriteLine($"========================================\n");

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
                    Url.Content("~/assets/images/church-001.jpg")
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
        /// 使用連接池優化，委派給輔助方法執行各步驟
        /// </summary>
        [HttpPost]
        [Route("/Authentication/ProcessLineBinding")]
        public async Task<IActionResult> ProcessLineBinding(LineBindingViewModel model)
        {
            try
            {
                // 步驟 0: 從 InMemoryContext 同步 LINE Profile 資料到 model
                SyncLineProfileToModel(model);

                System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 最終 model 資料:");
                System.Diagnostics.Debug.WriteLine($"  - FullName: {model.FullName}");
                System.Diagnostics.Debug.WriteLine($"  - Mobile: {model.Mobile}");
                System.Diagnostics.Debug.WriteLine($"  - LineUserId: {model.LineUserId}");
                System.Diagnostics.Debug.WriteLine($"  - DisplayName: {model.DisplayName ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"  - PictureUrl: {model.PictureUrl ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"  - StatusMessage: {model.StatusMessage ?? "(null)"}");

                // 步驟 1: 驗證必填欄位
                var validationResult = ValidateLineBindingModel(model);
                if (validationResult != null)
                    return validationResult;

                IOrganizationService service = null;
                try
                {
                    // 步驟 2: 從連接池獲取連接
                    service = GetConnection();

                    // 步驟 3: 檢查 LINE ID 是否已綁定
                    var existingBindingResult = await CheckExistingLineBinding(service, model.LineUserId);
                    if (existingBindingResult != null)
                        return existingBindingResult;

                    // 步驟 4: 查詢並匹配現有聯絡人
                    var matchedContact = await FindMatchingContactByNameAndMobile(service, model.FullName, model.Mobile);

                    if (matchedContact != null)
                    {
                        // 步驟 5a: 更新現有聯絡人
                        return await UpdateExistingContactWithLineBinding(service, matchedContact, model);
                    }

                    // 步驟 5b: 無法找到確定要綁定的聯絡人，交由輔助方法處理（包含錯誤回覆或建立新聯絡人）
                    return await HandleNoMatchAndMaybeCreateAsync(service, model);
                }
                catch (FaultException<OrganizationServiceFault> ex)
                {
                    return HandleCrmServiceException(ex);
                }
                catch (TimeoutException ex)
                {
                    return HandleTimeoutException(ex);
                }
                finally
                {
                    ReleaseConnection(service);
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 發生未預期的錯誤: {e.Message}");
                return HandleError(e, "ProcessLineBinding");
            }
        }

        #region ProcessLineBinding 輔助方法
        /// <summary>
        /// 將 InMemoryContext.LineBindingViewModel 的 LINE profile 資料同步至傳入的 model（若 model 未填寫）
        /// </summary>
        private void SyncLineProfileToModel(LineBindingViewModel model)
        {
            if (model == null) return;

            var src = InMemoryContext?.LineBindingViewModel;
            if (src == null) return;

            // 同步 DisplayName、PictureUrl、StatusMessage、LineUserId
            if (string.IsNullOrWhiteSpace(model.DisplayName) && !string.IsNullOrWhiteSpace(src.DisplayName))
            {
                model.DisplayName = src.DisplayName;
                System.Diagnostics.Debug.WriteLine($"[SyncLineProfileToModel] 同步 DisplayName: {model.DisplayName}");
            }

            if (string.IsNullOrWhiteSpace(model.PictureUrl) && !string.IsNullOrWhiteSpace(src.PictureUrl))
            {
                model.PictureUrl = src.PictureUrl;
                System.Diagnostics.Debug.WriteLine($"[SyncLineProfileToModel] 同步 PictureUrl: {model.PictureUrl}");
            }

            if (string.IsNullOrWhiteSpace(model.StatusMessage) && !string.IsNullOrWhiteSpace(src.StatusMessage))
            {
                model.StatusMessage = src.StatusMessage;
                System.Diagnostics.Debug.WriteLine($"[SyncLineProfileToModel] 同步 StatusMessage: {model.StatusMessage}");
            }

            if (string.IsNullOrWhiteSpace(model.LineUserId) && !string.IsNullOrWhiteSpace(src.LineUserId))
            {
                model.LineUserId = src.LineUserId;
                System.Diagnostics.Debug.WriteLine($"[SyncLineProfileToModel] 同步 LineUserId: {model.LineUserId}");
            }

            // 若 model.FullName 未帶值，且 InMemoryContext 有 FullName，則優先填入
            if (string.IsNullOrWhiteSpace(model.FullName) && !string.IsNullOrWhiteSpace(src.FullName))
            {
                model.FullName = src.FullName;
                System.Diagnostics.Debug.WriteLine($"[SyncLineProfileToModel] 同步 FullName: {model.FullName}");
            }

            // 若 model.Mobile 未帶值，且 InMemoryContext 有 Mobile，則優先填入
            if (string.IsNullOrWhiteSpace(model.Mobile) && !string.IsNullOrWhiteSpace(src.Mobile))
            {
                model.Mobile = src.Mobile;
                System.Diagnostics.Debug.WriteLine($"[SyncLineProfileToModel] 同步 Mobile: {model.Mobile}");
            }
        }

        /// <summary>
        /// 驗證 LINE 綁定模型的必填欄位
        /// </summary>
        private IActionResult ValidateLineBindingModel(LineBindingViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.FullName))
                return Json(new { status = "0", message = "主要姓名必填" });

            if (string.IsNullOrWhiteSpace(model.Mobile))
                return Json(new { status = "0", message = "行動電話必填" });

            if (string.IsNullOrWhiteSpace(model.LineUserId))
                return Json(new { status = "0", message = "LINE User ID 遺失" });

            return null; // 驗證通過
        }

        // 若在檢查 LINE ID 時找到僅為 (Line) 標記的佔位聯絡人，
        // 暫存該 Entity 以便在後續把資料合併後將佔位聯絡人設為 Inactive
        private Entity _placeholderLineContact = null;

        /// <summary>
        /// 確認現有綁定
        /// </summary>
        private async Task<IActionResult> CheckExistingLineBinding(IOrganizationService service, string lineUserId)
        {
            System.Diagnostics.Debug.WriteLine($"[CheckExistingLineBinding] 檢查 LINE ID 是否已綁定: {lineUserId}");

            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "fullname"),
                Criteria = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And,
                    Conditions =
                    {
                        new ConditionExpression("new_lineid", ConditionOperator.Equal, lineUserId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                },
                TopCount = 1
            };

            var results = await Task.Run(() => service.RetrieveMultiple(query));

            if (results.Entities.Count > 0)
            {
                var found = results.Entities[0];
                var existingName = found.GetAttributeValue<string>("fullname");
                System.Diagnostics.Debug.WriteLine($"[CheckExistingLineBinding] LINE ID 已綁定至: {existingName}");

                // 決策：若 existingName 非空且不包含 "(Line)"，拒絕綁定
                if (!string.IsNullOrWhiteSpace(existingName) && !existingName.Contains("(Line)"))
                {
                    return Json(new { status = "0", message = $"此 LINE 帳號已綁定至 {existingName}" });
                }

                // 若姓名包含 (Line) 表示為系統建立的佔位聯絡人，暫存以便之後停用
                if (!string.IsNullOrWhiteSpace(existingName) && existingName.Contains("(Line)"))
                {
                    _placeholderLineContact = found;
                    System.Diagnostics.Debug.WriteLine($"[CheckExistingLineBinding] 找到佔位聯絡人 (Line)，ID: {_placeholderLineContact.Id}");
                }
            }

            return null; // 未找到已綁定的帳號或找到可替換的佔位聯絡人，繼續後續處理
        }
        /// <summary>
        /// 根據姓名和手機號碼查詢並匹配現有聯絡人
        /// </summary>
        private async Task<Entity> FindMatchingContactByNameAndMobile(IOrganizationService service, string fullName, string mobile)
        {
            System.Diagnostics.Debug.WriteLine($"[FindMatchingContactByNameAndMobile] 檢查姓名是否已存在: {fullName}");

            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "fullname", "mobilephone"),
                Criteria = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And,
                    Conditions =
                    {
                        new ConditionExpression("fullname", ConditionOperator.Equal, fullName),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };

            var results = await Task.Run(() => service.RetrieveMultiple(query));

            if (results.Entities.Count == 0)
            {
                // 透過全名搜尋，系統沒有這個名字
                return null;
            }

            // 透過全名搜尋，系統有這個名字
            System.Diagnostics.Debug.WriteLine($"[FindMatchingContactByNameAndMobile] 找到 {results.Entities.Count} 個同名聯絡人");

            // 嘗試以手機號碼匹配（只比對數字）
            var normalizedInputMobile = ExtractDigits(mobile);
            foreach (var contact in results.Entities)
            {
                var mobilePhone = contact.Contains("mobilephone") ? contact.GetAttributeValue<string>("mobilephone") : string.Empty;
                var normalizedMobilePhone = ExtractDigits(mobilePhone);

                if (!string.IsNullOrEmpty(normalizedInputMobile) && normalizedMobilePhone == normalizedInputMobile)
                {
                    System.Diagnostics.Debug.WriteLine($"[FindMatchingContactByNameAndMobile] 找到匹配的聯絡人，手機: {mobilePhone}");
                    return contact;
                }
            }

            // 若找不到完全匹配的手機，優先回傳沒有手機號碼的聯絡人（可能為佔位或未填寫）
            foreach (var contact in results.Entities)
            {
                var mobilePhone = contact.Contains("mobilephone") ? contact.GetAttributeValue<string>("mobilephone") : string.Empty;
                if (string.IsNullOrEmpty(mobilePhone))
                {
                    System.Diagnostics.Debug.WriteLine($"[FindMatchingContactByNameAndMobile] 找到沒有手機號碼的聯絡人，ID: {contact.Id}");
                    return contact;
                }
            }

            // 若找不到完全匹配的手機，且每個同名聯絡人都有手機號碼，則無法確定要綁定哪一個
            // 回傳 null 以便上層方法提示使用者手機號碼不匹配
            System.Diagnostics.Debug.WriteLine($"[FindMatchingContactByNameAndMobile] ⚠️ 找到 {results.Entities.Count} 個同名聯絡人，但手機號碼均不匹配");
            System.Diagnostics.Debug.WriteLine($"[FindMatchingContactByNameAndMobile] 輸入的手機號碼（僅數字）: {normalizedInputMobile}");

            // 記錄所有同名聯絡人的手機號碼供診斷
            for (int i = 0; i < results.Entities.Count; i++)
            {
                var contact = results.Entities[i];
                var mobilePhone = contact.Contains("mobilephone") ? contact.GetAttributeValue<string>("mobilephone") : string.Empty;
                var normalizedDbMobile = ExtractDigits(mobilePhone);
                System.Diagnostics.Debug.WriteLine($"[FindMatchingContactByNameAndMobile]   聯絡人 {i + 1}: 手機={mobilePhone}, 僅數字={normalizedDbMobile}");
            }

            return null;
        }
        /// <summary>
        /// 更新現有聯絡人，綁定 LINE ID
        /// 若之前存在同一 LINE ID 的佔位聯絡人（fullname 含 (Line)），
        /// 會將該佔位聯絡人設為 Inactive（停用）以避免重複紀錄
        /// </summary>
        private async Task<IActionResult> UpdateExistingContactWithLineBinding(
            IOrganizationService service,
            Entity contact,
            LineBindingViewModel model)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 更新現有聯絡人的 LINE ID");

            // 若先前檢查到佔位聯絡人且與欲更新的 contact 不同，將佔位聯絡人停用
            if (_placeholderLineContact != null && _placeholderLineContact.Id != contact.Id)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 停用佔位聯絡人，ID: {_placeholderLineContact.Id}");

                    var inactiveEntity = new Entity("contact") { Id = _placeholderLineContact.Id };
                    // statecode = 1 (Inactive)
                    inactiveEntity["statecode"] = new OptionSetValue(1);
                    // statuscode: 2 為一般 Inactive 狀態
                    inactiveEntity["statuscode"] = new OptionSetValue(2);

                    await Task.Run(() => service.Update(inactiveEntity));

                    System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 佔位聯絡人已停用");
                }
                catch (Exception ex)
                {
                    // 停用失敗不應阻斷主要綁定流程，記錄錯誤並繼續
                    System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 停用佔位聯絡人失敗: {ex.Message}");
                }
                finally
                {
                    _placeholderLineContact = null; // 清除暫存
                }
            }

            // 綁定 LINE ID
            contact["new_lineid"] = model.LineUserId;

            // 更新聯絡人手機號碼（若提供）
            if (!string.IsNullOrWhiteSpace(model.Mobile))
            {
                // 儲存原始輸入的手機號碼到 CRM 的 mobilephone 欄位
                contact["mobilephone"] = ExtractDigits(model.Mobile.Trim());
                System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 更新 mobilephone: {model.Mobile}");
            }

            // 同步 LINE Profile 的其他欄位到 Contact
            if (!string.IsNullOrWhiteSpace(model.DisplayName))
            {
                contact["new_line_displayname"] = model.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(model.PictureUrl))
            {
                contact["new_line_picture_url"] = model.PictureUrl;
            }

            if (!string.IsNullOrWhiteSpace(model.StatusMessage))
            {
                contact["new_line_status_message"] = model.StatusMessage;
            }

            // 備份 LINE ID
            contact["new_lineid_backup"] = model.LineUserId;

            // 設定 LINE 類型為個人（維持與其他模組一致的欄位值）
            contact["new_line_type"] = "個人";

            // 標記為尚未透過系統註冊（依照既有邏輯可視情況調整）
            contact["new_line_register"] = false;

            if (!string.IsNullOrWhiteSpace(model.OtherName))
                contact["new_other_name"] = model.OtherName;

            await Task.Run(() => service.Update(contact));

            System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 綁定成功");

            return Json(new { status = "1", message = $"已成功綁定 LINE 至現有帳號:{model.FullName}" });
        }

        /// <summary>
        /// 建立新聯絡人並綁定 LINE ID
        /// </summary>
        private async Task<IActionResult> CreateNewContactWithLineBinding(
            IOrganizationService service,
            LineBindingViewModel model)
        {
            System.Diagnostics.Debug.WriteLine($"[CreateNewContactWithLineBinding] 建立新聯絡人");

            var newContact = new Entity("contact");
            newContact["lastname"] = model.FullName;
            newContact["mobilephone"] = ExtractDigits(model.Mobile);
            newContact["new_lineid"] = model.LineUserId;

            // 寫入 LINE Profile 的其他欄位
            if (!string.IsNullOrWhiteSpace(model.DisplayName))
                newContact["new_line_displayname"] = model.DisplayName;

            if (!string.IsNullOrWhiteSpace(model.PictureUrl))
                newContact["new_line_picture_url"] = model.PictureUrl;

            if (!string.IsNullOrWhiteSpace(model.StatusMessage))
                newContact["new_line_status_message"] = model.StatusMessage;

            // 備份 LINE ID
            newContact["new_lineid_backup"] = model.LineUserId;

            // LINE 類型與註冊狀態
            newContact["new_line_type"] = "個人";
            newContact["new_line_register"] = false;

            if (!string.IsNullOrWhiteSpace(model.OtherName))
                newContact["new_other_name"] = model.OtherName;

            var newContactId = await Task.Run(() => service.Create(newContact));

            System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 更新現有聯絡人的 LINE ID");

            // 若先前檢查到佔位聯絡人且與欲更新的 contact 不同，將佔位聯絡人停用
            if (_placeholderLineContact != null && _placeholderLineContact.Id != newContactId)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 停用佔位聯絡人，ID: {_placeholderLineContact.Id}");

                    var inactiveEntity = new Entity("contact") { Id = _placeholderLineContact.Id };
                    // statecode = 1 (Inactive)
                    inactiveEntity["statecode"] = new OptionSetValue(1);
                    // statuscode: 2 為一般 Inactive 狀態
                    inactiveEntity["statuscode"] = new OptionSetValue(2);

                    await Task.Run(() => service.Update(inactiveEntity));

                    System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 佔位聯絡人已停用");
                }
                catch (Exception ex)
                {
                    // 停用失敗不應阻斷主要綁定流程，記錄錯誤並繼續
                    System.Diagnostics.Debug.WriteLine($"[UpdateExistingContactWithLineBinding] 停用佔位聯絡人失敗: {ex.Message}");
                }
                finally
                {
                    _placeholderLineContact = null; // 清除暫存
                }
            }


            if (newContactId != Guid.Empty)
            {
                System.Diagnostics.Debug.WriteLine($"[CreateNewContactWithLineBinding] 新聯絡人建立成功，ID: {newContactId}");
                return Json(new { status = "1", message = $"註冊成功！歡迎 {model.FullName} 加入好牧人" });
            }

            System.Diagnostics.Debug.WriteLine($"[CreateNewContactWithLineBinding] 建立聯絡人失敗");
            return Json(new { status = "0", message = "註冊失敗，請稍後再試" });
        }

        /// <summary>
        /// 當找不到匹配的聯絡人時，處理可能的情境：
        /// - 若系統存在同名聯絡人但手機不匹配，提醒使用者確認手機或聯絡管理員
        /// - 否則建立新聯絡人
        /// </summary>
        private async Task<IActionResult> HandleNoMatchAndMaybeCreateAsync(IOrganizationService service, LineBindingViewModel model)
        {
            // 重新查詢同名聯絡人數量以判斷是否存在同名但手機不匹配的情況
            var nameCheckQuery = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid"),
                Criteria = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And,
                    Conditions =
                    {
                        new ConditionExpression("fullname", ConditionOperator.Equal, model.FullName),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };

            var nameCheckResults = await Task.Run(() => service.RetrieveMultiple(nameCheckQuery));

            if (nameCheckResults.Entities.Count > 0)
            {
                // 有同名聯絡人但手機不匹配，提示使用者採取後續動作
                System.Diagnostics.Debug.WriteLine($"[HandleNoMatchAndMaybeCreateAsync] 找到 {nameCheckResults.Entities.Count} 個同名聯絡人，但手機號碼不匹配");

                return Json(new
                {
                    status = "0",
                    message = $"系統找到 {nameCheckResults.Entities.Count} 位名為「{model.FullName}」的聯絡人，但您輸入的手機號碼與系統中的紀錄不符。\n\n" +
                             "請確認：\n" +
                             "1. 您輸入的手機號碼是否正確\n" +
                             "2. 若您的手機號碼已更換，請聯絡系統管理員更新資料庫中的手機號碼\n" +
                             "3. 若您是新註冊會員，請使用不同的姓名以避免重複"
                });
            }

            // 若沒有同名聯絡人，則建立新聯絡人並綁定
            return await CreateNewContactWithLineBinding(service, model);
        }


        #endregion

        /// <summary>
        /// 處理 CRM 服務異常
        /// </summary>
        private IActionResult HandleCrmServiceException(FaultException<OrganizationServiceFault> ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] CRM 服務異常: {ex.Detail?.Message ?? ex.Message}");
            return Json(new { status = "0", message = $"系統服務異常: {ex.Detail?.Message ?? ex.Message}" });
        }

        /// <summary>
        /// 處理連接超時異常
        /// </summary>
        private IActionResult HandleTimeoutException(TimeoutException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProcessLineBinding] 連接超時: {ex.Message}");
            return Json(new { status = "0", message = "系統連接超時，請稍後再試" });
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
        /// 使用連接池優化效能，減少連接創建時間
        /// </summary>
        private (bool isValid, string contactId, string errorMessage) ValidateUserCredentials(GalleryViewModel viewModel)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 開始驗證 - 帳號: {viewModel?.Account}");

                string contactIdString = "";

                if (viewModel.Account != "")
                {
                    // 透過帳號密碼登入 - 使用連接池優化
                    System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 使用帳號密碼登入");

                    IOrganizationService service = null;
                    try
                    {
                        // 從連接池獲取連接（耗時約 5ms，相比創建新連接的 500ms 大幅提升）
                        service = GetConnection();

                        // 直接使用 CRM SDK 查詢（避免透過 ToolUtility 創建新連接）
                        var query = new QueryExpression("contact")
                        {
                            ColumnSet = new ColumnSet("contactid", "new_app_pass"),
                            Criteria = new FilterExpression
                            {
                                FilterOperator = LogicalOperator.And,
                                Conditions =
                                {
                                    new ConditionExpression("new_app_acount", ConditionOperator.Equal, viewModel.Account),
                                    new ConditionExpression("statecode", ConditionOperator.Equal, 0) // 只查詢啟用的聯絡人
                                }
                            },
                            TopCount = 1 // 只需要一筆結果
                        };

                        var results = service.RetrieveMultiple(query);

                        if (results.Entities.Count == 0)
                        {
                            // 帳號不存在
                            System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 帳號錯誤");
                            return (false, "", "帳號錯誤");
                        }

                        var contact = results.Entities[0];
                        var storedPassword = contact.Contains("new_app_pass")
                            ? contact.GetAttributeValue<string>("new_app_pass")
                            : null;

                        // 檢查密碼
                        if (string.IsNullOrEmpty(storedPassword))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 系統沒有設定密碼");
                            return (false, "", "系統沒有設定密碼");
                        }

                        if (storedPassword != viewModel.Password)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 密碼錯誤");
                            return (false, "", "密碼錯誤");
                        }

                        // 驗證成功
                        contactIdString = contact.Id.ToString();
                        System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 驗證成功，Contact ID: {contactIdString}");
                    }
                    catch (FaultException<OrganizationServiceFault> ex)
                    {
                        // CRM 服務異常
                        System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] CRM 服務異常: {ex.Detail?.Message ?? ex.Message}");
                        return (false, "", $"系統服務異常: {ex.Detail?.Message ?? ex.Message}");
                    }
                    catch (TimeoutException ex)
                    {
                        // 連接超時
                        System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 連接超時: {ex.Message}");
                        return (false, "", "系統連接超時，請稍後再試");
                    }
                    finally
                    {
                        // 歸還連接到池（非常重要！確保連接重用）
                        ReleaseConnection(service);
                    }
                }
                else
                {
                    // 透過 LINE ID 登入
                    System.Diagnostics.Debug.WriteLine($"[ValidateUserCredentials] 使用 LINE ID 登入");
                    contactIdString = "透過Line Id 登入";
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
        /// 使用連接池優化效能，減少連接創建時間
        /// </summary>
        private async Task<(Entity loginContact, string fullName)> RetrieveUserData(
            string contactIdString,
            GalleryViewModel viewModel)
        {
            Entity loginContact = null;
            string fullName = "";

            IOrganizationService service = null;
            try
            {
                // 從連接池獲取連接
                service = GetConnection();

                if (contactIdString != "透過Line Id 登入")
                {
                    // 使用者透過網頁的帳號密碼登入
                    System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 使用 Contact ID 查詢: {contactIdString}");

                    // 直接使用 CRM SDK 查詢，獲取完整的聯絡人資料
                    loginContact = service.Retrieve("contact", new Guid(contactIdString), new ColumnSet(true));
                    fullName = loginContact.Contains("fullname")
                        ? loginContact.GetAttributeValue<string>("fullname")
                        : "";

                    System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 查詢成功，姓名: {fullName}");
                }
                else
                {
                    // 使用者透過 LINE ID 登入
                    System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 使用 LINE ID 查詢: {InMemoryContext.LineBindingViewModel.LineUserId}");

                    // 使用 QueryExpression 查詢 LINE ID 綁定的聯絡人
                    var query = new QueryExpression("contact")
                    {
                        ColumnSet = new ColumnSet(true), // 需要完整資料供後續使用
                        Criteria = new FilterExpression
                        {
                            FilterOperator = LogicalOperator.And,
                            Conditions =
                            {
                                new ConditionExpression("new_lineid", ConditionOperator.Equal,
                                    InMemoryContext.LineBindingViewModel.LineUserId),
                                new ConditionExpression("statecode", ConditionOperator.Equal, 0) // 只查詢啟用的聯絡人
                            }
                        },
                        TopCount = 1 // 只需要一筆結果
                    };

                    var results = service.RetrieveMultiple(query);

                    if (results.Entities.Count > 0)
                    {
                        loginContact = results.Entities[0];
                        fullName = loginContact.Contains("fullname")
                            ? loginContact.GetAttributeValue<string>("fullname")
                            : "";

                        // 設定 LINE 登入的帳密
                        viewModel.Account = "LineIdLogin";
                        viewModel.Password = InMemoryContext.LineBindingViewModel.LineUserId;

                        System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] LINE 登入查詢成功，姓名: {fullName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 找不到對應的 LINE 使用者");
                    }
                }
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                // CRM 服務異常
                System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] CRM 服務異常: {ex.Detail?.Message ?? ex.Message}");
                throw new Exception($"取得使用者資料失敗: {ex.Detail?.Message ?? ex.Message}", ex);
            }
            catch (TimeoutException ex)
            {
                // 連接超時
                System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 連接超時: {ex.Message}");
                throw new Exception("取得使用者資料超時，請稍後再試", ex);
            }
            catch (Exception ex)
            {
                // 一般異常
                System.Diagnostics.Debug.WriteLine($"[RetrieveUserData] 發生異常: {ex.Message}");
                throw;
            }
            finally
            {
                // 歸還連接到池（非常重要！確保連接重用）
                ReleaseConnection(service);
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
            IOrganizationService service = null;
            try
            {
                // 從連接池獲取連接
                service = GetConnection();
                System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 已從連接池獲取 IOrganizationService");

                // 設定多個組長處理需要的資料
                System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 呼叫 SetupListManager - 開始時間: {DateTime.Now:HH:mm:ss.fff}");
                try
                {
                    // ✅ 傳入 organizationService 避免內部為 null
                    InMemoryContext.ListManager.SetupListManager(
                        viewModel.Account,
                        viewModel.Password,
                        DateTime.Now,
                        service); // 傳入連接池中的服務
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
            finally
            {
                // 歸還連接到池（非常重要！確保連接重用）
                if (service != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SetupSystemData] 歸還 IOrganizationService 到連接池");
                    ReleaseConnection(service);
                }
            }
        }

        /// <summary>
        /// 判斷顯示視圖類型
        /// </summary>
        private string DetermineDisplayViewType()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 開始判斷顯示視圖類型");

                // 控制 Navigation 下拉項目
                ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
                ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
                ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;

                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] UserType={ViewBag.UserType}");
                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] LoginType={InMemoryContext.ListManager.LoginType}");

                // 透過取得多小組網頁需要的資料之後，判斷這是多小組還是單一小組長的回報
                string displayViewType = InMemoryContext.ListManager.GetDisplayViewType();

                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] GetDisplayViewType() 回傳值: '{displayViewType ?? "null"}'");

                // ✅ 保護性檢查: 如果 displayViewType 是 null 或空字串，設定預設值
                if (string.IsNullOrEmpty(displayViewType))
                {
                    System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 警告: displayViewType 為空，使用預設值");

                    // 根據 LoginType 決定預設值
                    if (InMemoryContext.ListManager.LoginType == "小組長")
                    {
                        displayViewType = "IntegrateView";
                        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 小組長預設值: IntegrateView");
                    }
                    else
                    {
                        displayViewType = "MultiGroupView";
                        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 非小組長預設值: MultiGroupView");
                    }
                }

                if (displayViewType == "IntegrateView")
                {
                    System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 視圖類型為 IntegrateView，開始設定整合資料");
                    // 得知這是單一小組長的回報，所以就直接下載整合式網頁所需的資料
                    try
                    {
                        InMemoryContext.ListManager.SetupIntegrateData(InMemoryContext.ListManager.ActiveListId);
                        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] SetupIntegrateData 完成");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] SetupIntegrateData 失敗: {ex.Message}");
                        // 即使失敗也繼續，不影響登入
                    }
                }

                // 根據登入類型和幸福小組狀態調整顯示類型
                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] HappyType={InMemoryContext.HappyGroupDataManager.HappyType}");

                if (InMemoryContext.ListManager.LoginType != "小組長" &&
                    InMemoryContext.HappyGroupDataManager.HappyType == "有幸福小組名單")
                {
                    System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 調整為 HappyGroupView");
                    displayViewType = "HappyGroupView";
                }

                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 最終視圖類型: {displayViewType}");

                return displayViewType;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 發生異常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[DetermineDisplayViewType] 堆疊追蹤: {ex.StackTrace}");

                // 發生異常時，返回安全的預設值
                return "IntegrateView";
            }
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

        #region LINE ID 儲存

        /// <summary>
        /// 儲存 LINE 使用者 ID（用於身分綁定頁面）
        /// 檢查用戶是否已綁定，並返回綁定狀態
        /// 使用連接池優化效能
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

                // 檢查用戶是否已綁定 - 使用連接池優化
                IOrganizationService service = null;
                try
                {
                    service = GetConnection();

                    System.Diagnostics.Debug.WriteLine($"[SaveUserId] 檢查 LINE ID 是否已綁定: {UserLineId}");

                    // 步驟 3: 檢查 LINE ID 是否已綁定
                    var existingBindingResult = await CheckExistingLineBinding(service, UserLineId);
                    if (existingBindingResult != null)
                    {
                        // 用戶已綁定
                        return existingBindingResult;
                    }
                    else
                    {
                        // 用戶尚未綁定 
                        System.Diagnostics.Debug.WriteLine($"[SaveUserId] 用戶尚未綁定");
                        return Json(new
                        {
                            status = "1",
                            message = "請完成身分綁定註冊"
                        });
                    }
                }
                finally
                {
                    ReleaseConnection(service);
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveUserId] 發生錯誤: {e.Message}");
                return HandleError(e, "SaveUserId");
            }
        }

        /// <summary>
        /// 只保留字串中的數字字元 (0-9)，用於比較手機號碼一致性
        /// </summary>
        private string ExtractDigits(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return Regex.Replace(input, "\\D", string.Empty);
        }
        #endregion
    }
}
