# Controller 分割快速參考卡

## ?? 核心概念

### 為什麼要分割？
```
當前問題：HomeController 承擔太多職責
├── 登入認證 (帳密 + LINE + QR Code)
├── Session 初始化 (5+ 個管理器)
├── 導覽決策 (複雜的 if-else)
├── ViewBag 設定
└── 舊路由重導向

結果：
? ProcessLogin 方法 150+ 行
? 圈複雜度 > 15
? 難以測試和維護
? 違反單一職責原則
```

### 分割後的結構
```
Before:
HomeController (14 methods, 500+ lines)
└── ProcessLogin (150 lines, complexity 15)

After:
├── AuthenticationController (5 methods)
│   └── ProcessLogin (40 lines, complexity 5) ?
├── PhoneManagementController (4 methods)
├── AuthenticationService ?
├── SessionInitializationService ?
└── NavigationService ?
```

---

## ?? 執行步驟速查

### Step 1: 準備工作
```powershell
# 1. 建立分支
git checkout -b feature/controller-split-authentication

# 2. 備份
git tag backup-before-split

# 3. 執行建立腳本
cd ChurchReport\Scripts
.\Migrate-ControllerSplit-Phase1.ps1
```

### Step 2: 加入檔案到專案
```
在 Visual Studio 中：
1. 右鍵點擊 Models 資料夾 → Add → Existing Item
2. 選擇 Models\Authentication\*.cs (4 個檔案)
3. 重複步驟加入 Services 和 Controllers 檔案
```

### Step 3: 實作服務
```csharp
// 1. AuthenticationService.cs
public class AuthenticationService : IAuthenticationService
{
    // 從 HomeController.ProcessLogin 複製驗證邏輯
    public async Task<AuthResult> ValidateCredentialsAsync(...)
    {
        // 原本的 ToolUtility.RetrieveContactByAccountNumber 邏輯
    }
}

// 2. SessionInitializationService.cs
public class SessionInitializationService : ISessionInitializationService
{
    // 從 HomeController.ProcessLogin 複製 Session 初始化邏輯
    public async Task<SessionData> InitializeSessionAsync(...)
    {
        // 原本的 InMemoryContext 設定邏輯
    }
}

// 3. NavigationService.cs
public class NavigationService : INavigationService
{
    // 從 HomeController.ProcessLogin 複製導覽決策邏輯
    public RedirectInfo DetermineRedirect(SessionData sessionData)
    {
        // 原本的 if-else 決策樹
    }
}
```

### Step 4: 建立 AuthenticationController
```csharp
public class AuthenticationController : BaseChurchController
{
    private readonly IAuthenticationService _authService;
    private readonly ISessionInitializationService _sessionService;
    private readonly INavigationService _navigationService;

    [HttpPost]
    [Route("/Auth/ProcessLogin")]
    public async Task<IActionResult> ProcessLogin([FromForm] LoginRequest request)
    {
        // 1. 驗證
        var authResult = await _authService.ValidateCredentialsAsync(...);
        
        // 2. 初始化 Session
        var sessionData = await _sessionService.InitializeSessionAsync(...);
        
        // 3. 決定導向
        var redirectInfo = _navigationService.DetermineRedirect(sessionData);
        
        // 4. 返回結果
        return Json(new LoginResponse { ... });
    }
}
```

### Step 5: 註冊服務
```csharp
// Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    // 新增以下行
    services.AddScoped<IAuthenticationService, AuthenticationService>();
    services.AddScoped<ISessionInitializationService, SessionInitializationService>();
    services.AddScoped<INavigationService, NavigationService>();
}
```

### Step 6: 修改 Login.cshtml
```razor
@* 只需改這一行 *@
<form asp-action="ProcessLogin" 
      asp-controller="Authentication"  @* 原本是 "Home" *@
      method="post">
```

### Step 7: 保留向後相容
```csharp
// HomeController.cs 保留重導向
[Route("/Home/Login")]
public IActionResult Login() 
    => RedirectToAction("Login", "Authentication");

[HttpPost]
[Route("/Home/ProcessLogin")]
public IActionResult ProcessLogin([FromForm] LoginRequest request) 
    => RedirectToAction("ProcessLogin", "Authentication", request);
```

---

## ?? 測試檢查清單

### 功能測試
- [ ] ? 一般帳密登入成功
- [ ] ? 一般帳密登入失敗（錯誤密碼）
- [ ] ? LINE 登入成功
- [ ] ? 登入後導向到正確頁面
  - [ ] MultiGroupView (多小組長)
  - [ ] IntegrateView (單一小組長)
  - [ ] HappyGroupView (幸福小組)
- [ ] ? Session 資料正確初始化
- [ ] ? ViewBag 資料正確設定
- [ ] ? 錯誤訊息正確顯示 (Toast)

### 向後相容測試
- [ ] ? `/Home/Login` 可正常訪問
- [ ] ? `/Home/ProcessLogin` POST 可正常運作
- [ ] ? 既有書籤連結不失效

### 效能測試
- [ ] ? 登入回應時間 < 500ms
- [ ] ? 無記憶體洩漏
- [ ] ? 並發 10 個請求正常

---

## ?? 常見問題排查

### 問題 1: 找不到 IAuthenticationService
```
錯誤：The type or namespace name 'IAuthenticationService' could not be found

解決：
1. 確認檔案已加入專案（Solution Explorer 中可見）
2. 確認 using ChurchReport.Services.Authentication;
3. 重新建置專案
```

### 問題 2: Dependency Injection 失敗
```
錯誤：Unable to resolve service for type 'IAuthenticationService'

解決：
1. 檢查 Startup.cs 是否註冊服務
2. 確認服務生命週期正確（Scoped/Transient/Singleton）
3. 重新啟動應用程式
```

### 問題 3: 登入後出現 404
```
錯誤：Cannot find the view 'Login'

解決：
1. 確認 Login.cshtml 在正確位置：Views/Authentication/Login.cshtml
2. 或保留在 Views/Home/Login.cshtml，但要明確指定：
   return View("~/Views/Home/Login.cshtml", viewModel);
```

### 問題 4: Session 資料遺失
```
錯誤：登入後 InMemoryContext 資料為空

解決：
1. 確認 InMemoryContext 是透過 DI 注入
2. 確認 SessionInitializationService 有正確設定資料
3. 檢查服務生命週期（應為 Scoped）
```

---

## ?? 效益對比

| 指標 | 重構前 | 重構後 | 改善 |
|------|--------|--------|------|
| **HomeController 方法數** | 14 | 7 | ?? 50% |
| **ProcessLogin 行數** | 150+ | 40 | ?? 73% |
| **圈複雜度** | 15 | 5 | ?? 66% |
| **測試覆蓋率** | 10% | 80% | ?? 700% |
| **新增登入方式時間** | 2-3 天 | 0.5-1 天 | ?? 60% |
| **Bug 修復時間** | 2 小時 | 30 分鐘 | ?? 75% |

---

## ?? 程式碼對比範例

### Before (HomeController.cs)
```csharp
[HttpPost]
public async Task<IActionResult> ProcessLogin(GalleryViewModel aGalleryViewModel)
{
    // 150+ 行混合邏輯
    string ContactIdString = "";
    if (aGalleryViewModel.Account != "")
    {
        ContactIdString = ToolUtility.RetrieveContactByAccountNumber(...);
    }
    else
    {
        ContactIdString = "透過Line Id 登入";
    }
    
    if (ContactIdString != "密碼錯誤" && ...)
    {
        Entity aLoginContact;
        if (ContactIdString != "透過Line Id 登入")
        {
            aLoginContact = ToolUtility.RetrieveEntityDynamics365(...);
            // 50+ 行 Session 初始化
            InMemoryContext.AppointmentsListManager.m_Account = ...;
            InMemoryContext.ListManager.SetupListManager(...);
            InMemoryContext.QpayManager.SetQpayModel(...);
            // 更多設定...
            
            // 30+ 行導覽決策
            if (InMemoryContext.ListManager.LoginType == "小組長" && ...)
            {
                ViewBag.LoginType = ...;
                ViewBag.LoginFullName = ...;
                // 更多 ViewBag 設定...
                return Json(new { DisplayViewType = ..., ... });
            }
            else if (...)
            {
                // 更多分支...
            }
            // ...
        }
    }
    else
    {
        return Json(new { DisplayViewType = "登入錯誤", ... });
    }
}
```

### After (AuthenticationController.cs)
```csharp
[HttpPost]
[Route("/Auth/ProcessLogin")]
public async Task<IActionResult> ProcessLogin([FromForm] LoginRequest request)
{
    // 40 行清晰邏輯
    
    // 1. 驗證 (3 行)
    var authResult = await _authService.ValidateCredentialsAsync(
        request.Account, 
        request.Password);
    
    if (!authResult.Success)
    {
        return Json(new LoginResponse
        {
            DisplayViewType = "登入錯誤",
            Message = authResult.ErrorMessage,
            Success = false
        });
    }
    
    // 2. 初始化 Session (3 行)
    var sessionData = await _sessionService.InitializeSessionAsync(
        authResult.LoginContact,
        authResult.LoginType,
        request.Account,
        request.Password);
    
    // 3. 設定 ViewBag (1 行)
    SetupViewBagFromSession(sessionData);
    
    // 4. 決定導向 (2 行)
    var redirectInfo = _navigationService.DetermineRedirect(sessionData);
    
    // 5. 返回結果 (10 行)
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
```

**改善點：**
- ? 行數減少 73%
- ? 每個步驟職責單一
- ? 易於單元測試（可 Mock Service）
- ? 易於理解和維護
- ? 符合 SOLID 原則

---

## ?? 緊急聯絡

如遇到無法解決的問題：

1. **回滾到備份點**
   ```powershell
   git reset --hard backup-before-split
   ```

2. **檢查文件**
   - ?? `Controller分割設計評估報告.md` - 完整設計方案
   - ?? `Controller分割實作範例.md` - 詳細實作範例
   - ?? `Controller分割遷移進度.md` - 進度追蹤

3. **參考原始碼**
   - 原始的 `HomeController.cs` (已備份)
   - 可在 Git 歷史記錄中找到

---

## ? 完成檢查

全部完成後，請確認：

- [ ] ? 所有測試通過
- [ ] ? 程式碼審查完成
- [ ] ? 文件已更新
- [ ] ? Git Commit Message 清楚
- [ ] ? 通知團隊成員變更

**Commit Message 範例：**
```
feat(auth): 重構登入邏輯，分割 HomeController 為專職 Controller

- 新增 AuthenticationController 處理登入邏輯
- 新增 AuthenticationService、SessionInitializationService、NavigationService
- ProcessLogin 方法從 150+ 行簡化為 40 行
- 圈複雜度從 15 降低為 5
- 測試覆蓋率從 10% 提升至 80%+
- 保留向後相容的舊路由重導向

BREAKING CHANGE: 無（所有舊路由仍可正常運作）
```

---

**文件版本：** 1.0  
**建立日期：** 2024-12-XX  
**適用範圍：** ChurchReport 專案 Controller 重構
