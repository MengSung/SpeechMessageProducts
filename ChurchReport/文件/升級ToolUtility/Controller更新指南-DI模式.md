# Controller 更新指南 - Dependency Injection 模式實現

## 概述
本文檔說明如何更新所有繼承自 `BaseChurchController` 的 Controller，使其正確使用 Dependency Injection 模式。

## 已完成的更新

### 1. ? BaseChurchController.cs
已更新為使用 `IToolUtilityProvider` 進行依賴注入：

```csharp
protected readonly IToolUtilityProvider _toolUtilityProvider;
protected ToolUtilityClass ToolUtility => _toolUtilityProvider.GetToolUtility();

protected BaseChurchController(
    IHttpContextAccessor httpContextAccessor,
    IMemoryCache memoryCache,
    IPayment paymentService,
    IToolUtilityProvider toolUtilityProvider)
{
    _toolUtilityProvider = toolUtilityProvider ?? throw new ArgumentNullException(nameof(toolUtilityProvider));
    // ...
}
```

### 2. ? HomeController.cs
已更新建構函數接受 `IToolUtilityProvider` 參數

### 3. ? Startup.cs
已在 `ConfigureServices` 中註冊 ToolUtility 服務：
```csharp
services.AddToolUtility();
```

## 需要更新的 Controller 列表

以下所有繼承自 `BaseChurchController` 的 Controller 都需要更新其建構函數：

### 1. AuthenticationController.cs
**位置**: `ChurchReport\Controllers\AuthenticationController.cs`

**修改前**:
```csharp
public AuthenticationController(
    IHttpContextAccessor httpContextAccessor, 
    IMemoryCache memoryCache, 
    IPayment qpayService)
    : base(httpContextAccessor, memoryCache, qpayService)
{
}
```

**修改後**:
```csharp
using ToolUtilityNameSpace.DependencyInjection;

public AuthenticationController(
    IHttpContextAccessor httpContextAccessor, 
    IMemoryCache memoryCache, 
    IPayment qpayService,
    IToolUtilityProvider toolUtilityProvider)
    : base(httpContextAccessor, memoryCache, qpayService, toolUtilityProvider)
{
}
```

### 2. EquipmentController.cs
**位置**: `ChurchReport\Controllers\EquipmentController.cs`

**修改**: 同 AuthenticationController

### 3. SmallGroupController.cs (如果存在)
**修改**: 同 AuthenticationController

### 4. PersonalController.cs (如果存在)
**修改**: 同 AuthenticationController

### 5. DedicationController.cs (如果存在)
**修改**: 同 AuthenticationController

### 6. NewPersonController.cs (如果存在)
**修改**: 同 AuthenticationController

### 7. SchedulerController.cs (如果存在)
**修改**: 同 AuthenticationController

### 8. QrCodeController.cs (如果存在)
**修改**: 同 AuthenticationController

### 9. ListManagementController.cs (如果存在)
**修改**: 同 AuthenticationController

### 10. PhoneBindingController.cs (如果存在)
**修改**: 同 AuthenticationController

### 11. DedicationAuditController.cs (如果存在)
**修改**: 同 AuthenticationController

## 統一修改模式

所有繼承自 `BaseChurchController` 的 Controller 都需要：

### 步驟 1: 添加 using 語句
```csharp
using ToolUtilityNameSpace.DependencyInjection;
```

### 步驟 2: 修改建構函數
```csharp
// 修改前
public YourController(
    IHttpContextAccessor httpContextAccessor, 
    IMemoryCache memoryCache, 
    IPayment paymentService)
    : base(httpContextAccessor, memoryCache, paymentService)
{
}

// 修改後
public YourController(
    IHttpContextAccessor httpContextAccessor, 
    IMemoryCache memoryCache, 
    IPayment paymentService,
    IToolUtilityProvider toolUtilityProvider)
    : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider)
{
}
```

## 批量搜索和修改步驟

### 使用 Visual Studio 全局搜索

1. **搜索所有繼承 BaseChurchController 的類別**
   - 按 `Ctrl+Shift+F`
   - 搜索: `: BaseChurchController`
   - 記錄所有匹配的 Controller

2. **對每個 Controller 進行修改**
   - 添加 `using ToolUtilityNameSpace.DependencyInjection;`
   - 在建構函數參數中添加 `IToolUtilityProvider toolUtilityProvider`
   - 在 base() 調用中添加 `toolUtilityProvider`

## PowerShell 批量修改腳本

```powershell
# 警告：執行前請備份代碼！

$controllerFiles = Get-ChildItem -Path "ChurchReport\Controllers" -Filter "*Controller.cs"

foreach ($file in $controllerFiles) {
    $content = Get-Content $file.FullName -Raw
    
    # 檢查是否繼承 BaseChurchController
    if ($content -match ': BaseChurchController') {
        Write-Host "Processing: $($file.Name)"
        
        # 添加 using 語句（如果不存在）
        if ($content -notmatch 'using ToolUtilityNameSpace.DependencyInjection;') {
            $content = $content -replace '(using ToolUtilityNameSpace;)', "$1`r`nusing ToolUtilityNameSpace.DependencyInjection;"
        }
        
        # 修改建構函數
        # 這個正則表達式需要根據實際情況調整
        $pattern = '(\s+public\s+\w+Controller\s*\(\s*IHttpContextAccessor\s+\w+,\s*IMemoryCache\s+\w+,\s*IPayment\s+\w+)\)'
        $replacement = '$1, IToolUtilityProvider toolUtilityProvider)'
        $content = $content -replace $pattern, $replacement
        
        # 修改 base() 調用
        $pattern = '(: base\([^,]+,\s*[^,]+,\s*[^)]+)\)'
        $replacement = '$1, toolUtilityProvider)'
        $content = $content -replace $pattern, $replacement
        
        # 保存文件
        Set-Content -Path $file.FullName -Value $content -Encoding UTF8
    }
}

Write-Host "批量更新完成！"
```

## 手動修改檢查清單

對每個 Controller 文件：

- [ ] 已添加 `using ToolUtilityNameSpace.DependencyInjection;`
- [ ] 建構函數參數已添加 `IToolUtilityProvider toolUtilityProvider`
- [ ] base() 調用已添加 `toolUtilityProvider` 參數
- [ ] 編譯無錯誤
- [ ] 功能測試通過

## 驗證步驟

### 1. 編譯檢查
```bash
dotnet build
```
應該沒有編譯錯誤。

### 2. 運行時檢查
```csharp
// 在任何 Controller 的 Action 中添加日誌
var instance1 = ToolUtility;
var instance2 = _toolUtilityProvider.GetToolUtility();
Console.WriteLine($"Same instance: {ReferenceEquals(instance1, instance2)}"); 
// 應該輸出 True，證明是 Singleton
```

### 3. 功能測試
測試每個 Controller 的主要功能，確認沒有破壞現有行為。

## 常見問題

### Q: 為什麼要修改所有 Controller？
A: 因為 `BaseChurchController` 的建構函數簽名已更改，所有子類都需要更新以匹配新的簽名。

### Q: 如果忘記添加 toolUtilityProvider 參數會怎樣？
A: 會出現編譯錯誤，因為無法找到匹配的 base 建構函數。

### Q: 是否需要修改 Controller 內部的代碼？
A: 不需要，只需要修改建構函數。`ToolUtility` 屬性的使用方式完全不變。

### Q: DI 是否會影響性能？
A: 不會，實際上還會提升性能，因為整個應用程式只有一個 ToolUtilityClass 實例。

## 特殊情況處理

### 如果 Controller 有自定義初始化邏輯

**修改前**:
```csharp
public MyController(
    IHttpContextAccessor httpContextAccessor, 
    IMemoryCache memoryCache, 
    IPayment paymentService)
    : base(httpContextAccessor, memoryCache, paymentService)
{
    // 自定義初始化
    _myService = new MyService();
}
```

**修改後**:
```csharp
public MyController(
    IHttpContextAccessor httpContextAccessor, 
    IMemoryCache memoryCache, 
    IPayment paymentService,
    IToolUtilityProvider toolUtilityProvider)
    : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider)
{
    // 自定義初始化保持不變
    _myService = new MyService();
}
```

## 完成後的檢查清單

- [ ] 所有 Controller 建構函數已更新
- [ ] 所有文件已添加必要的 using 語句
- [ ] Solution 可以成功編譯
- [ ] 單元測試通過（如果有）
- [ ] 整合測試通過
- [ ] 所有頁面可以正常訪問
- [ ] 功能測試通過
- [ ] 確認 Singleton 行為正常

## 回滾計劃

如果更新後出現問題，可以快速回滾：

1. 從 Git 恢復 BaseChurchController.cs
2. 從 Git 恢復所有修改過的 Controller
3. 從 Startup.cs 中移除 `services.AddToolUtility();`

```bash
git checkout HEAD -- ChurchReport/Controllers/BaseChurchController.cs
git checkout HEAD -- ChurchReport/Controllers/*Controller.cs
git checkout HEAD -- ChurchReport/Startup.cs
```

## 支援資源

- [設計模式文檔](./ToolUtilityClass重構實現-SingletonFactoryDI.md)
- [Factory 模式指南](./批量更新腳本-Factory模式.md)
- [ASP.NET Core DI 官方文檔](https://docs.microsoft.com/zh-tw/aspnet/core/fundamentals/dependency-injection)
