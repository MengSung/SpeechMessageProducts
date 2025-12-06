# Controller 分割實作範例

## ?? 目錄結構

建議的新目錄結構：

```
ChurchReport
├── Controllers
│   ├── Authentication
│   │   └── AuthenticationController.cs      // 新增
│   ├── UserManagement
│   │   └── PhoneManagementController.cs     // 新增
│   ├── BaseChurchController.cs              // 保留
│   ├── HomeController.cs                    // 簡化，只保留重導向
│   └── ... (其他 Controller)
│
├── Services
│   ├── Authentication
│   │   ├── IAuthenticationService.cs        // 新增
│   │   ├── AuthenticationService.cs         // 新增
│   │   ├── ISessionInitializationService.cs // 新增
│   │   └── SessionInitializationService.cs  // 新增
│   ├── Navigation
│   │   ├── INavigationService.cs            // 新增
│   │   └── NavigationService.cs             // 新增
│   └── PhoneManagement
│       ├── IPhoneManagementService.cs       // 新增
│       └── PhoneManagementService.cs        // 新增
│
└── Models
    ├── Authentication
    │   ├── LoginRequest.cs                  // 新增
    │   ├── LoginResponse.cs                 // 新增
    │   ├── AuthResult.cs                    // 新增
    │   └── SessionData.cs                   // 新增
    └── PhoneManagement
        ├── PhoneUpdateRequest.cs            // 新增
        └── PhoneUpdateResult.cs             // 新增
```

---

## 1?? AuthenticationController 完整實作

```csharp
using ChurchReport.Models.Authentication;
using ChurchReport.Services.Authentication;
using ChurchReport.Services.Navigation;
using ChurchReport.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChurchReport.Controllers.Authentication
{
    /// <summary>
    /// 認證控制器 - 處理所有登入相關邏輯
    /// </summary>
    public class AuthenticationController : BaseChurchController
    {
        private readonly IAuthenticationService _authService;
        private readonly ISessionInitializationService _sessionService;
        private readonly INavigationService _navigationService;

        #region 建構函式

        public AuthenticationController(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IPayment qpayService,
            IAuthenticationService authService,
            ISessionInitializationService sessionService,
            INavigationService navigationService)
            : base(httpContextAccessor, memoryCache, qpayService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        }

        #endregion

        #region 登入頁面

        /// <summary>
        /// 顯示登入頁面
        /// </summary>
        [HttpGet]
        [Route("/Auth/Login")]
        public async Task<IActionResult> Login()
        {
            try
            {
                var images = new List<string>
                {
                    Url.Content("~/assets/images/church-001.jpg")
                };

                return View(new GalleryViewModel
                {
                    Images = images
                });
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(Login));
            }
        }

        #endregion

        #region 處理登入

        /// <summary>
        /// 處理登入請求 (帳密登入或 LINE 登入)
        /// </summary>
        [HttpPost]
        [Route("/Auth/ProcessLogin")]
        public async Task<IActionResult> ProcessLogin([FromForm] LoginRequest request)
        {
            try
            {
                // 1. 驗證登入資訊
                var authResult = await ValidateLoginAsync(request);
                if (!authResult.IsSuccess)
                {
                    return Json(new LoginResponse
                    {
                        DisplayViewType = "登入錯誤",
                        Message = authResult.ErrorMessage,
                        Success = false
                    });
                }

                // 2. 初始化 Session
                var sessionData = await _sessionService.InitializeSessionAsync(
                    authResult.LoginContact,
                    authResult.LoginType,
                    request.Account,
                    request.Password);

                // 3. 設定 ViewBag
                SetupViewBagFromSession(sessionData);

                // 4. 決定導向頁面
                var redirectInfo = _navigationService.DetermineRedirect(sessionData);

                // 5. 返回成功結果
                return Json(new LoginResponse
                {
                    DisplayViewType = redirectInfo.ViewType,
                    ActiveListId = redirectInfo.ActiveListId,
                    Message = $"歡迎 {authResult.FullName} 登入成功!",
                    FullName = authResult.FullName,
                    Account = request.Account,
                    Password = request.Password,
                    Success = true
                });
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(ProcessLogin));
            }
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 驗證登入資訊
        /// </summary>
        private async Task<AuthResult> ValidateLoginAsync(LoginRequest request)
        {
            // 判斷登入類型
            if (!string.IsNullOrEmpty(request.Account) && request.Account != "LineIdLogin")
            {
                // 一般帳密登入
                return await _authService.ValidateCredentialsAsync(
                    request.Account, 
                    request.Password);
            }
            else if (request.Account == "LineIdLogin" && !string.IsNullOrEmpty(request.Password))
            {
                // LINE ID 登入 (Password 欄位存放 LineUserId)
                return await _authService.ValidateLineIdAsync(request.Password);
            }
            else
            {
                // 從 InMemoryContext 取得 LINE ID
                return await _authService.ValidateLineIdAsync(
                    InMemoryContext.LineBindingViewModel.LineUserId);
            }
        }

        /// <summary>
        /// 從 Session 資料設定 ViewBag
        /// </summary>
        private void SetupViewBagFromSession(SessionData sessionData)
        {
            ViewBag.LoginType = sessionData.LoginType;
            ViewBag.LoginFullName = sessionData.FullName;
            ViewBag.HappyType = sessionData.HasHappyGroup ? "有幸福小組名單" : "沒幸福小組名單";
            ViewBag.FeeType = sessionData.HasFeeData ? "有繳費點名" : "無繳費點名";
            ViewBag.FeeDataListCount = sessionData.HasFeeData ? "繳費與點名已有資料" : "繳費與點名尚無資料";
            ViewBag.DisplayNavigation = "顯示牧養回報項目";
            ViewBag.SchedulerView = "不是單純行事曆";
            ViewBag.UserType = sessionData.UserType;

            SetMultiGroupLayoutParameter();
        }

        #endregion

        #region LINE 登入專用

        /// <summary>
        /// LINE 登入頁面
        /// </summary>
        [HttpGet]
        [Route("/Auth/LineLogin")]
        public IActionResult LineIdLoginView()
        {
            try
            {
                var images = new List<string>
                {
                    Url.Content("~/assets/images/church-001.jpg")
                };

                InMemoryContext.LineBindingViewModel.Images = images;

                return View(InMemoryContext.LineBindingViewModel);
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(LineIdLoginView));
            }
        }

        /// <summary>
        /// 處理 LINE 登入 (從 LIFF 接收 Line User ID)
        /// </summary>
        [HttpPost]
        [Route("/Auth/ProcessLineLogin")]
        public async Task<IActionResult> ProcessLineLogin([FromBody] LineLoginRequest request)
        {
            try
            {
                // 儲存 LINE 資訊到 InMemoryContext
                InMemoryContext.LineBindingViewModel.LineUserId = request.LineUserId;
                InMemoryContext.LineBindingViewModel.DisplayName = request.DisplayName;

                // 呼叫統一的登入處理
                return await ProcessLogin(new LoginRequest
                {
                    Account = "LineIdLogin",
                    Password = request.LineUserId
                });
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(ProcessLineLogin));
            }
        }

        #endregion

        #region 登出

        /// <summary>
        /// 登出
        /// </summary>
        [HttpPost]
        [Route("/Auth/Logout")]
        public IActionResult Logout()
        {
            try
            {
                // 清除 Session
                _sessionService.ClearSession();

                return RedirectToAction(nameof(Login));
            }
            catch (Exception e)
            {
                return HandleError(e, nameof(Logout));
            }
        }

        #endregion
    }
}
```

---

## 2?? AuthenticationService 實作

```csharp
using ChurchReport.Models.Authentication;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;
using ToolUtilityNameSpace;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// 認證服務介面
    /// </summary>
    public interface IAuthenticationService
    {
        Task<AuthResult> ValidateCredentialsAsync(string account, string password);
        Task<AuthResult> ValidateLineIdAsync(string lineUserId);
    }

    /// <summary>
    /// 認證服務實作
    /// </summary>
    public class AuthenticationService : IAuthenticationService
    {
        private readonly ToolUtilityClass _toolUtility;

        public AuthenticationService()
        {
            _toolUtility = new ToolUtilityClass("DYNAMICS365-9.0");
        }

        /// <summary>
        /// 驗證帳號密碼
        /// </summary>
        public async Task<AuthResult> ValidateCredentialsAsync(string account, string password)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 呼叫 CRM 驗證
                    string contactIdString = _toolUtility.RetrieveContactByAccountNumber(account, password);

                    // 檢查驗證結果
                    if (contactIdString == "密碼錯誤")
                    {
                        return AuthResult.CreateFail("密碼錯誤，請重新輸入");
                    }
                    else if (contactIdString == "系統沒有設定密碼")
                    {
                        return AuthResult.CreateFail("系統沒有設定密碼，請聯絡管理員");
                    }
                    else if (contactIdString == "帳號錯誤")
                    {
                        return AuthResult.CreateFail("帳號不存在，請確認後再試");
                    }

                    // 取得連絡人實體
                    Entity loginContact = _toolUtility.RetrieveEntityDynamics365(
                        "contact", 
                        new Guid(contactIdString));

                    string fullName = _toolUtility.GetEntityStringAttribute(ref loginContact, "fullname");

                    return AuthResult.CreateSuccess(
                        loginContact, 
                        fullName, 
                        LoginType.AccountPassword);
                }
                catch (Exception ex)
                {
                    return AuthResult.CreateFail($"驗證過程發生錯誤: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 驗證 LINE User ID
        /// </summary>
        public async Task<AuthResult> ValidateLineIdAsync(string lineUserId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(lineUserId))
                    {
                        return AuthResult.CreateFail("LINE User ID 不可為空");
                    }

                    // 透過 LINE ID 查詢連絡人
                    Entity loginContact = _toolUtility.RetrieveContactEntityByLineUserId(lineUserId);

                    if (loginContact == null)
                    {
                        return AuthResult.CreateFail("此 LINE 帳號尚未綁定教會會員資料");
                    }

                    string fullName = _toolUtility.GetEntityStringAttribute(ref loginContact, "fullname");

                    return AuthResult.CreateSuccess(
                        loginContact, 
                        fullName, 
                        LoginType.LineId);
                }
                catch (Exception ex)
                {
                    return AuthResult.CreateFail($"LINE 登入驗證發生錯誤: {ex.Message}");
                }
            });
        }
    }
}
```

---

## 3?? SessionInitializationService 實作

```csharp
using ChurchReport.Models.Authentication;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// Session 初始化服務介面
    /// </summary>
    public interface ISessionInitializationService
    {
        Task<SessionData> InitializeSessionAsync(
            Entity loginContact, 
            LoginType loginType, 
            string account, 
            string password);

        void ClearSession();
    }

    /// <summary>
    /// Session 初始化服務實作
    /// </summary>
    public class SessionInitializationService : ISessionInitializationService
    {
        private readonly InMemoryDataContextSmallGroup _inMemoryContext;

        public SessionInitializationService(InMemoryDataContextSmallGroup inMemoryContext)
        {
            _inMemoryContext = inMemoryContext ?? throw new ArgumentNullException(nameof(inMemoryContext));
        }

        /// <summary>
        /// 初始化使用者 Session
        /// </summary>
        public async Task<SessionData> InitializeSessionAsync(
            Entity loginContact,
            LoginType loginType,
            string account,
            string password)
        {
            return await Task.Run(() =>
            {
                var sessionData = new SessionData
                {
                    LoginContact = loginContact,
                    LoginType = loginType.ToString(),
                    Account = loginType == LoginType.LineId ? "LineIdLogin" : account,
                    Password = loginType == LoginType.LineId ? 
                        _inMemoryContext.LineBindingViewModel.LineUserId : password
                };

                // 1. 設定行事曆管理器
                InitializeAppointmentManager(sessionData);

                // 2. 設定小組清單管理器
                InitializeListManager(sessionData);

                // 3. 設定金流管理器
                InitializeQpayManager(loginContact);

                // 4. 設定個人資料管理器
                InitializePersonalInfoManager(loginContact);

                // 5. 設定繳費清單
                InitializeFeeList(sessionData);

                // 6. 設定 ViewBag 資料
                PopulateViewBagData(sessionData);

                return sessionData;
            });
        }

        /// <summary>
        /// 清除 Session
        /// </summary>
        public void ClearSession()
        {
            _inMemoryContext.AppointmentsListManager.Clear();
            _inMemoryContext.ListManager.Clear();
            _inMemoryContext.QpayManager.Clear();
            _inMemoryContext.PersonalInfomationModel.Clear();
            _inMemoryContext.FeeList.Clear();
        }

        #region 私有初始化方法

        private void InitializeAppointmentManager(SessionData sessionData)
        {
            _inMemoryContext.AppointmentsListManager.m_Account = sessionData.Account;
            _inMemoryContext.AppointmentsListManager.m_Password = sessionData.Password;
            _inMemoryContext.AppointmentsListManager.m_LoginContact = sessionData.LoginContact;
            _inMemoryContext.AppointmentsListManager.SetupAppointmentList();
        }

        private void InitializeListManager(SessionData sessionData)
        {
            _inMemoryContext.ListManager.SetupListManager(
                sessionData.Account,
                sessionData.Password,
                DateTime.Now);

            sessionData.DisplayViewType = _inMemoryContext.ListManager.GetDisplayViewType();
            sessionData.ActiveListId = _inMemoryContext.ListManager.ActiveListId;

            // 如果是單一小組長，下載整合式資料
            if (sessionData.DisplayViewType == "IntegrateView")
            {
                _inMemoryContext.ListManager.SetupIntegrateData(sessionData.ActiveListId);
            }
        }

        private void InitializeQpayManager(Entity loginContact)
        {
            if (loginContact != null)
            {
                _inMemoryContext.QpayManager.LoginType = "網頁登入";
                _inMemoryContext.QpayManager.SetQpayModel(loginContact);
            }
        }

        private void InitializePersonalInfoManager(Entity loginContact)
        {
            _inMemoryContext.PersonalInfomationModel.m_LoginContact = loginContact;
        }

        private void InitializeFeeList(SessionData sessionData)
        {
            _inMemoryContext.FeeList.SetupLessonList(
                sessionData.Account,
                sessionData.Password);

            sessionData.HasFeeData = _inMemoryContext.FeeList.FeeDataList != null &&
                                     _inMemoryContext.FeeList.FeeDataList.Count > 0;
        }

        private void PopulateViewBagData(SessionData sessionData)
        {
            sessionData.FullName = _inMemoryContext.ListManager.LoginFullName;
            sessionData.UserType = _inMemoryContext.AppointmentsListManager.UserType;
            sessionData.HasHappyGroup = _inMemoryContext.HappyGroupDataManager.HappyType == "有幸福小組名單";
        }

        #endregion
    }
}
```

---

## 4?? Model 定義

```csharp
// Models/Authentication/LoginRequest.cs
namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// 登入請求模型
    /// </summary>
    public class LoginRequest
    {
        public string Account { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// LINE 登入請求模型
    /// </summary>
    public class LineLoginRequest
    {
        public string LineUserId { get; set; }
        public string DisplayName { get; set; }
    }
}

// Models/Authentication/LoginResponse.cs
namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// 登入回應模型
    /// </summary>
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string DisplayViewType { get; set; }
        public string ActiveListId { get; set; }
        public string Message { get; set; }
        public string FullName { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
    }
}

// Models/Authentication/AuthResult.cs
namespace ChurchReport.Models.Authentication
{
    using Microsoft.Xrm.Sdk;

    /// <summary>
    /// 認證結果
    /// </summary>
    public class AuthResult
    {
        public bool IsSuccess { get; set; }
        public Entity LoginContact { get; set; }
        public string FullName { get; set; }
        public LoginType LoginType { get; set; }
        public string ErrorMessage { get; set; }

        public static AuthResult CreateSuccess(Entity contact, string fullName, LoginType type)
        {
            return new AuthResult
            {
                IsSuccess = true,
                LoginContact = contact,
                FullName = fullName,
                LoginType = type
            };
        }

        public static AuthResult CreateFail(string errorMessage)
        {
            return new AuthResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// 登入類型
    /// </summary>
    public enum LoginType
    {
        AccountPassword,
        LineId,
        QrCode
    }
}

// Models/Authentication/SessionData.cs
namespace ChurchReport.Models.Authentication
{
    using Microsoft.Xrm.Sdk;

    /// <summary>
    /// Session 資料
    /// </summary>
    public class SessionData
    {
        public Entity LoginContact { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string LoginType { get; set; }
        public string DisplayViewType { get; set; }
        public string ActiveListId { get; set; }
        public string UserType { get; set; }
        public bool HasHappyGroup { get; set; }
        public bool HasFeeData { get; set; }
    }
}
```

---

## 5?? Startup.cs 註冊服務

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // ... 既有服務 ...

    // 認證服務
    services.AddScoped<IAuthenticationService, AuthenticationService>();
    services.AddScoped<ISessionInitializationService, SessionInitializationService>();
    services.AddScoped<INavigationService, NavigationService>();

    // ... 其他服務 ...
}
```

---

## 6?? Login.cshtml 修改

```razor
@* 修改 form 的 asp-controller *@
<form data-ajax="true" 
      data-ajax-begin="onBegin" 
      data-ajax-success="onSuccess" 
      asp-action="ProcessLogin" 
      asp-controller="Authentication"  @* 改為新的 Controller *@
      method="post" 
      novalidate>
    
    @* 表單內容不變 *@
    
</form>
```

---

## 7?? HomeController 簡化版

```csharp
public class HomeController : BaseChurchController
{
    public HomeController(
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache memoryCache,
        IPayment qpayService)
        : base(httpContextAccessor, memoryCache, qpayService)
    {
    }

    #region 向後相容重導向

    /// <summary>
    /// 重導向到新的登入 Controller
    /// </summary>
    [Route("/Home/Login")]
    public IActionResult Login() 
        => RedirectToAction("Login", "Authentication");

    /// <summary>
    /// 重導向到新的登入處理
    /// </summary>
    [HttpPost]
    [Route("/Home/ProcessLogin")]
    public IActionResult ProcessLogin([FromForm] LoginRequest request) 
        => RedirectToAction("ProcessLogin", "Authentication", request);

    // ... 其他重導向方法 ...

    #endregion
}
```

---

## ? 驗證清單

重構完成後，請確認：

### 功能驗證
- [ ] 一般帳密登入正常運作
- [ ] LINE 登入正常運作
- [ ] 登入後正確導向對應頁面
- [ ] Session 資料正確初始化
- [ ] ViewBag 資料正確設定
- [ ] 錯誤訊息正確顯示

### 向後相容性
- [ ] 舊的 `/Home/Login` 路由可正常重導向
- [ ] 舊的 `/Home/ProcessLogin` 路由可正常重導向
- [ ] 既有頁面連結不受影響

### 效能與品質
- [ ] 回應時間 < 500ms
- [ ] 無記憶體洩漏
- [ ] 單元測試覆蓋率 > 80%
- [ ] 程式碼符合 SOLID 原則

---

**文件版本：** 1.0  
**最後更新：** 2024-12-XX
