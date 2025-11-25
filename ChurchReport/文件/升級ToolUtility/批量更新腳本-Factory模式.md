# 批量更新腳本 - 將所有使用 new ToolUtilityClass() 改為 Factory 模式

## 已完成更新的文件
- ? ChurchReport\WebServiceConnector\DownloadIntegrateData.cs (需手動確認)
- ? ChurchReport\WebServiceConnector\LineBindingUtility.cs

## 需要更新的文件列表及修改指令

### 1. 添加 using 語句
在每個使用 ToolUtilityClass 的文件頂部添加：
```csharp
using ToolUtilityNameSpace.Factory;
```

### 2. 替換實例化代碼

#### 模式 1：無參數建構函數
```csharp
// 修改前
private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass();

// 修改後
private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance();
```

#### 模式 2：有參數建構函數
```csharp
// 修改前
private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365-9.0");

// 修改後
private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
```

#### 模式 3：在方法中創建
```csharp
// 修改前
public void SomeMethod()
{
    ToolUtilityClass tool = new ToolUtilityClass();
    // ...
}

// 修改後
public void SomeMethod()
{
    ToolUtilityClass tool = ToolUtilityFactory.GetInstance();
    // ...
}
```

## 待更新文件清單

### ChurchReport\WebServiceConnector\QPayProcessor.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`

### ChurchReport\WebServiceConnector\LineNotifyUtility.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`

### ChurchReport\Controllers\HomeController.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`
- **建議**: 改用 Dependency Injection 模式（見下方）

### ChurchReport\Models\QpayManager.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`

### ChurchReport\Tools\LineUtilityClass.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`

### ChurchReport\Tools\QPayDedicationBookingProcessor.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`

### ChurchReport\Tools\PersonalQrCodeUtility.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`

### ChurchReport\WebServiceConnector\NewPerson.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`

### ChurchReport\Controllers\AuthenticationController.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`
- **建議**: 改用 Dependency Injection 模式（見下方）

### ChurchReport\Controllers\EquipmentController.cs
- 搜索: `new ToolUtilityClass`
- 替換為: `ToolUtilityFactory.GetInstance`
- 添加: `using ToolUtilityNameSpace.Factory;`
- **建議**: 改用 Dependency Injection 模式（見下方）

## Controller 類別建議使用 Dependency Injection

### 修改前
```csharp
public class HomeController : Controller
{
    private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass();
    
    public IActionResult Index()
    {
        // 使用 m_ToolUtilityClass
    }
}
```

### 修改後（推薦）
```csharp
using ToolUtilityNameSpace.DependencyInjection;

public class HomeController : Controller
{
    private readonly IToolUtilityProvider _toolUtilityProvider;
    private ToolUtilityClass m_ToolUtilityClass => _toolUtilityProvider.GetToolUtility();
    
    public HomeController(IToolUtilityProvider toolUtilityProvider)
    {
        _toolUtilityProvider = toolUtilityProvider;
    }
    
    public IActionResult Index()
    {
        // 使用 m_ToolUtilityClass
    }
}
```

## Visual Studio 全局搜索和替換

### 步驟 1: 搜索所有使用 new ToolUtilityClass 的位置
1. 按 `Ctrl+Shift+F` 打開全局搜索
2. 搜索: `new ToolUtilityClass(`
3. 範圍選擇: 整個解決方案
4. 記錄所有匹配的文件

### 步驟 2: 添加 using 語句
對每個匹配的文件，在文件頂部添加：
```csharp
using ToolUtilityNameSpace.Factory;
```

### 步驟 3: 替換實例化代碼
使用 `Ctrl+H` 進行查找和替換：
- 查找: `new ToolUtilityClass\(\)`
- 替換為: `ToolUtilityFactory.GetInstance()`
- 使用正則表達式

對於帶參數的：
- 查找: `new ToolUtilityClass\("([^"]+)"\)`
- 替換為: `ToolUtilityFactory.GetInstance("$1")`
- 使用正則表達式

## 驗證步驟

### 1. 編譯檢查
```bash
# 在 Solution 目錄執行
dotnet build
```
應該沒有編譯錯誤，因為 `new ToolUtilityClass()` 現在是 `internal` 的。

### 2. 運行時檢查
啟動應用程式，確認功能正常工作。

### 3. 確認 Singleton 行為
在應用程式啟動時添加日誌：
```csharp
var instance1 = ToolUtilityFactory.GetInstance();
var instance2 = ToolUtilityFactory.GetInstance();
Console.WriteLine($"Same instance: {ReferenceEquals(instance1, instance2)}"); // 應該輸出 True
```

## 注意事項

1. **不要刪除原有的 m_ToolUtilityClass 變數名稱**，保持向後兼容
2. **Controller 建議使用 DI 模式**，更符合 ASP.NET Core 最佳實踐
3. **確保 Program.cs 已註冊服務**：
   ```csharp
   builder.Services.AddToolUtility();
   ```
4. **測試所有修改過的功能**，確保沒有破壞現有行為

## 常見問題

### Q: 為什麼要改成 Factory 模式？
A: 
- 確保整個應用程式只有一個 ToolUtilityClass 實例
- 節省記憶體
- 避免多次初始化帶來的性能開銷
- 便於集中管理和測試

### Q: 現有代碼是否需要大幅修改？
A: 
- 不需要，只需將 `new ToolUtilityClass()` 改為 `ToolUtilityFactory.GetInstance()`
- 其他代碼邏輯保持不變

### Q: 如何在單元測試中使用？
A: 
可以使用 `ToolUtilityFactory.ResetInstance()` 在每個測試後重置實例（僅供測試使用）

## PowerShell 批量修改腳本（謹慎使用）

```powershell
# 警告：執行前請備份代碼！

$files = Get-ChildItem -Path "ChurchReport" -Include *.cs -Recurse

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    
    # 檢查是否包含 new ToolUtilityClass
    if ($content -match 'new ToolUtilityClass') {
        Write-Host "Processing: $($file.FullName)"
        
        # 添加 using 語句（如果不存在）
        if ($content -notmatch 'using ToolUtilityNameSpace.Factory;') {
            $content = $content -replace '(using ToolUtilityNameSpace;)', "$1`r`nusing ToolUtilityNameSpace.Factory;"
        }
        
        # 替換 new ToolUtilityClass() 為 ToolUtilityFactory.GetInstance()
        $content = $content -replace 'new ToolUtilityClass\(\)', 'ToolUtilityFactory.GetInstance()'
        
        # 替換帶參數的版本
        $content = $content -replace 'new ToolUtilityClass\("([^"]+)"\)', 'ToolUtilityFactory.GetInstance("$1")'
        
        # 保存文件
        Set-Content -Path $file.FullName -Value $content
    }
}

Write-Host "批量更新完成！請檢查並測試所有修改。"
```

## 完成後檢查清單

- [ ] 所有文件都添加了 `using ToolUtilityNameSpace.Factory;`
- [ ] 所有 `new ToolUtilityClass()` 都改為 `ToolUtilityFactory.GetInstance()`
- [ ] Controller 考慮改用 Dependency Injection 模式
- [ ] Program.cs 已註冊 `AddToolUtility()` 服務
- [ ] Solution 可以成功編譯
- [ ] 應用程式可以正常運行
- [ ] 所有功能經過測試
- [ ] 確認 Singleton 行為正常
