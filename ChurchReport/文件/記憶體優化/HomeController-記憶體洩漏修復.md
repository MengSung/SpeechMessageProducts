# ?? 緊急: HomeController 記憶體洩漏問題

**嚴重程度**: ?? 高  
**發現日期**: 2025年1月  
**狀態**: ? 需要立即修復

---

## ?? 問題描述

### 問題位置
**文件**: `ChurchReport\Controllers\HomeController.cs`

### 洩漏模式
在多個向後兼容路由方法中，**手動創建了 Controller 實例但沒有釋放**。

---

## ? 問題代碼示例

### 示例 1: ProcessLoginRedirect
```csharp
[HttpPost]
[Route("/Home/ProcessLogin")]
public async Task<IActionResult> ProcessLoginRedirect(GalleryViewModel aGalleryViewModel)
{
    // ? 手動創建 AuthenticationController
    var authController = new AuthenticationController(
        HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
        HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
        HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
        HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
        HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool);
    
    return await authController.ProcessLogin(aGalleryViewModel);
    
    // ? 記憶體洩漏！authController 沒有被 Dispose
}
```

### 示例 2: SaveUserLineIdRedirect
```csharp
[HttpPost]
[Route("/Home/SaveUserLineId")]
public async Task<IActionResult> SaveUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
{
    // ? 手動創建 AuthenticationController
    var authController = new AuthenticationController(/* 參數 */);
    
    return await authController.SaveUserLineId(UserLineId, GroupId, RoomId, ViewType);
    
    // ? 記憶體洩漏！authController 沒有被 Dispose
}
```

### 示例 3: SetupUserLineIdRedirect
```csharp
[HttpPost]
[Route("/Home/SetupUserLineId")]
public IActionResult SetupUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
{
    // ? 手動創建 DedicationController
    var dedicationController = new DedicationController(/* 參數 */);
    
    return dedicationController.SetupUserLineId(UserLineId, GroupId, RoomId, ViewType);
    
    // ? 記憶體洩漏！dedicationController 沒有被 Dispose
}
```

---

## ?? 洩漏分析

### 為什麼會洩漏？

1. **Controller 實現了 IDisposable**
   - `BaseChurchController` 實現了 `IDisposable`
   - 所有繼承的 Controller 都是 IDisposable

2. **手動創建未釋放**
   - 使用 `new` 創建 Controller 實例
   - 沒有調用 `Dispose()` 釋放資源
   - ToolUtility、ConnectionPool 等資源未釋放

3. **累積效應**
   - 每次請求都會創建新實例
   - 資源持續累積
   - GC 無法回收（因為未釋放）

### 受影響的方法

| 方法 | 創建的 Controller | 影響 |
|------|------------------|------|
| ProcessLoginRedirect | AuthenticationController | ?? 高 |
| SaveUserLineIdRedirect | AuthenticationController | ?? 高 |
| ProcessLineBindingRedirect | AuthenticationController | ?? 高 |
| SaveUserIdRedirect | AuthenticationController | ?? 高 |
| SetupUserLineIdRedirect | DedicationController | ?? 高 |

---

## ? 修復方案

### 方案 1: 使用 using 語句（推薦）

```csharp
[HttpPost]
[Route("/Home/ProcessLogin")]
public async Task<IActionResult> ProcessLoginRedirect(GalleryViewModel aGalleryViewModel)
{
    // ? 使用 using 確保釋放
    using (var authController = new AuthenticationController(
        HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
        HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
        HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment,
        HttpContext.RequestServices.GetService(typeof(IToolUtilityProvider)) as IToolUtilityProvider,
        HttpContext.RequestServices.GetService(typeof(ICrmConnectionPool)) as ICrmConnectionPool))
    {
        return await authController.ProcessLogin(aGalleryViewModel);
    } // ? 自動調用 Dispose
}
```

### 方案 2: 使用 RedirectToAction（最佳）

**更好的做法**：直接重定向，讓 ASP.NET Core 管理 Controller 生命週期。

```csharp
[HttpPost]
[Route("/Home/ProcessLogin")]
public IActionResult ProcessLoginRedirect(GalleryViewModel aGalleryViewModel)
{
    // ? 使用 RedirectToAction，讓框架處理
    return RedirectToAction("ProcessLogin", "Authentication", aGalleryViewModel);
}
```

**注意**: RedirectToAction 不支持 POST 數據，需要改用其他方式。

### 方案 3: 提取共享服務（長期）

**最佳架構**：將業務邏輯提取到 Service 層，而非在 Controller 中互相調用。

```csharp
// 創建共享服務
public interface IAuthenticationService
{
    Task<IActionResult> ProcessLogin(GalleryViewModel model);
    Task<IActionResult> SaveUserLineId(string userLineId, string groupId, string roomId, string viewType);
}

// 在 Controller 中注入服務
public class HomeController : BaseChurchController
{
    private readonly IAuthenticationService _authService;
    
    public HomeController(
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache memoryCache,
        IPayment qpayService,
        IToolUtilityProvider toolUtilityProvider,
        ICrmConnectionPool connectionPool,
        IAuthenticationService authService)  // ? 注入服務
    : base(httpContextAccessor, memoryCache, qpayService, toolUtilityProvider, connectionPool)
    {
        _authService = authService;
    }
    
    [HttpPost]
    [Route("/Home/ProcessLogin")]
    public async Task<IActionResult> ProcessLoginRedirect(GalleryViewModel aGalleryViewModel)
    {
        // ? 使用服務，無需手動管理生命週期
        return await _authService.ProcessLogin(aGalleryViewModel);
    }
}
```

---

## ?? 立即修復（使用方案 1）

由於方案 2 和方案 3 需要較大改動，我們先使用**方案 1（using 語句）**立即修復洩漏。

### 修復清單

- [ ] ProcessLoginRedirect - 添加 using
- [ ] SaveUserLineIdRedirect - 添加 using
- [ ] ProcessLineBindingRedirect - 添加 using
- [ ] SaveUserIdRedirect - 添加 using
- [ ] SetupUserLineIdRedirect - 添加 using

---

## ?? 預期改善

### 修復前
- 每次請求創建 Controller：**未釋放**
- 累積的資源：**ToolUtility、ConnectionPool、InMemoryContext**
- 記憶體增長：**持續上升**

### 修復後
- 每次請求創建 Controller：**正確釋放**
- 累積的資源：**無**
- 記憶體增長：**穩定**

### 量化改善
- 記憶體洩漏：**-5 處高風險洩漏**
- 每次請求記憶體節省：**~2-5 MB**
- 長期運行穩定性：**顯著提升**

---

## ?? 後續改進

### 短期（1 週內）
1. ? 使用 using 語句修復所有手動創建的 Controller
2. ? 編譯驗證
3. ? 功能測試確保向後兼容

### 中期（1 個月內）
1. ? 重構為服務層架構
2. ? 移除 Controller 之間的直接調用
3. ? 使用 DI 注入共享服務

### 長期（3 個月內）
1. ? 完整的架構重構
2. ? 實現 CQRS 模式
3. ? 改善代碼可測試性

---

## ?? 實施計畫

### 步驟 1: 修復 HomeController
- 文件: `ChurchReport\Controllers\HomeController.cs`
- 修改: 5 個方法添加 using 語句
- 預計時間: 15 分鐘

### 步驟 2: 編譯驗證
```powershell
cd ChurchReport
dotnet build ChurchReport.csproj
```

### 步驟 3: 功能測試
- 測試所有向後兼容路由
- 確認沒有破壞現有功能
- 驗證記憶體使用量

### 步驟 4: 監測改善
```powershell
# 使用 dotnet-counters 監測
dotnet-counters monitor --process-id <PID> System.Runtime

# 關注指標:
# - GC Heap Size
# - Gen 2 GC Count
```

---

## ?? 注意事項

### 1. using 語句的限制
```csharp
// ? 錯誤：return 在 using 之外
using (var controller = new SomeController(/* ... */))
{
    var result = await controller.SomeMethod();
}
return result; // 錯誤：result 可能包含已釋放資源的引用

// ? 正確：return 在 using 內部
using (var controller = new SomeController(/* ... */))
{
    return await controller.SomeMethod();
} // Dispose 在 return 之後執行
```

### 2. 異步方法的 using
```csharp
// ? 正確：async/await 與 using 配合
public async Task<IActionResult> SomeMethod()
{
    using (var controller = new SomeController(/* ... */))
    {
        return await controller.ProcessAsync();
    }
}

// C# 8.0+ 可以使用 using 聲明（更簡潔）
public async Task<IActionResult> SomeMethod()
{
    using var controller = new SomeController(/* ... */);
    return await controller.ProcessAsync();
    // Dispose 在方法結束時自動調用
}
```

### 3. 返回值處理
確保返回的 `IActionResult` 不包含對已釋放 Controller 的引用。
大多數情況下這不是問題，因為 `IActionResult` 通常是獨立的。

---

## ?? 成功標準

### 編譯
- ? 無編譯錯誤
- ? 無編譯警告

### 功能
- ? 所有向後兼容路由正常工作
- ? 登入流程正常
- ? 數據保存正常

### 性能
- ? 記憶體使用量不再持續上升
- ? GC Gen 2 收集頻率降低
- ? 長時間運行穩定

---

**創建日期**: 2025年1月  
**優先級**: ?? 緊急  
**預計修復時間**: 15-30 分鐘  
**狀態**: ? 待修復  
**版本**: 1.0
