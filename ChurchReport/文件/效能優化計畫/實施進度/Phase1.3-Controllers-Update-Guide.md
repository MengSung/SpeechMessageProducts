# Phase 1.3: 批量更新 Controllers 使用連接池 - 指南

## ?? 更新概述

所有繼承自 `BaseChurchController` 的 Controller 都需要更新建構式以注入 `ICrmConnectionPool`。

---

## ?? 需要更新的 Controllers 清單

### 已完成 ?
1. ? `BaseChurchController` - 已添加連接池支援
2. ? `SmallGroupController` - 已更新建構式

### 待更新 ?
3. ? `PersonalController`
4. ? `DedicationController`
5. ? `DedicationAuditController`
6. ? `AuthenticationController`
7. ? `NewPersonController`
8. ? `EquipmentController`
9. ? `ListManagementController`
10. ? `PhoneBindingController`
11. ? `QrCodeController`
12. ? `AppointmentController`
13. ? `HomeController`
14. ? `MyPayController`
15. ? `TSPGController`

---

## ??? 更新步驟

### 步驟 1: 更新建構式簽名

**修改前**:
```csharp
public class ExampleController : BaseChurchController
{
    public ExampleController(
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache memoryCache,
        IPayment paymentService,
        IToolUtilityProvider toolUtilityProvider)
        : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider)
    {
    }
}
```

**修改後**:
```csharp
using ToolUtilityNameSpace.ConnectionOperations;

public class ExampleController : BaseChurchController
{
    public ExampleController(
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache memoryCache,
        IPayment paymentService,
        IToolUtilityProvider toolUtilityProvider,
        ICrmConnectionPool connectionPool)  // 添加連接池參數
        : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)
    {
    }
}
```

### 步驟 2: 添加必要的 using 語句

在檔案頂部添加:
```csharp
using ToolUtilityNameSpace.ConnectionOperations;
```

### 步驟 3: （可選）使用連接池進行查詢

如果 Controller 中有直接使用 `IOrganizationService` 的查詢，可以改用連接池：

**修改前**:
```csharp
public IActionResult GetWeeklyReport(Guid listId, DateTime sunday)
{
    var toolUtility = _toolUtilityProvider.GetToolUtility();
    var result = toolUtility.QueryWeeklyReportBySunday(sunday, listId);
    return Json(result);
}
```

**修改後**:
```csharp
public IActionResult GetWeeklyReport(Guid listId, DateTime sunday)
{
    IOrganizationService service = null;
    try
    {
        // 從連接池取得連接
        service = GetConnection();
        
        // 執行查詢
        var query = new QueryExpression("new_weekly_report")
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("new_list", ConditionOperator.Equal, listId),
                    new ConditionExpression("new_sunday", ConditionOperator.Equal, sunday)
                }
            }
        };
        
        var result = service.RetrieveMultiple(query);
        return Json(result.Entities);
    }
    finally
    {
        // 歸還連接（非常重要！）
        ReleaseConnection(service);
    }
}
```

---

## ?? 批量更新腳本模板

### PowerShell 腳本 (用於批量添加 using 和更新建構式)

```powershell
# 定義要更新的 Controllers 路徑
$controllersPath = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers"

# 定義要更新的檔案清單
$controllers = @(
    "PersonalController.cs",
    "DedicationController.cs",
    "DedicationAuditController.cs",
    "AuthenticationController.cs",
    "NewPersonController.cs",
    "EquipmentController.cs",
    "ListManagementController.cs",
    "PhoneBindingController.cs",
    "QrCodeController.cs",
    "AppointmentController.cs",
    "HomeController.cs",
    "MyPayController.cs",
    "TSPGController.cs"
)

foreach ($controller in $controllers) {
    $filePath = Join-Path $controllersPath $controller
    
    if (Test-Path $filePath) {
        Write-Host "更新 $controller..." -ForegroundColor Yellow
        
        $content = Get-Content $filePath -Raw
        
        # 檢查是否已經有 using 語句
        if ($content -notmatch "using ToolUtilityNameSpace\.ConnectionOperations;") {
            # 在其他 using 語句後添加
            $content = $content -replace "(using ToolUtilityNameSpace\.DependencyInjection;)", "`$1`r`nusing ToolUtilityNameSpace.ConnectionOperations;"
        }
        
        # 更新建構式簽名
        $content = $content -replace `
            "(\s+IToolUtilityProvider toolUtilityProvider\))\s*:\s*base\(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider\)", `
            "`$1,`r`n            ICrmConnectionPool connectionPool)`r`n        : base(httpContextAccessor, memoryCache, paymentService, toolUtilityProvider, connectionPool)"
        
        # 儲存更新後的內容
        Set-Content $filePath $content -NoNewline
        
        Write-Host "? $controller 已更新" -ForegroundColor Green
    }
    else {
        Write-Host "? 找不到檔案: $controller" -ForegroundColor Red
    }
}

Write-Host "`n批量更新完成！" -ForegroundColor Cyan
```

### 使用方式
1. 將上述 PowerShell 腳本保存為 `Update-Controllers.ps1`
2. 在 PowerShell 中執行: `.\Update-Controllers.ps1`
3. 檢查更新結果並編譯專案

---

## ? 驗證清單

更新完成後，請驗證以下項目：

### 1. 建置測試
```bash
dotnet build
```
- [ ] 專案編譯成功
- [ ] 無編譯錯誤
- [ ] 無編譯警告

### 2. 建構式簽名檢查
確認每個 Controller 建構式：
- [ ] 包含 `ICrmConnectionPool connectionPool` 參數
- [ ] 正確傳遞給基底類別
- [ ] 包含正確的 using 語句

### 3. 功能測試
- [ ] 登入功能正常
- [ ] 小組週報載入正常
- [ ] 資料查詢正常
- [ ] 資料儲存正常

### 4. 效能監控
- [ ] 檢查連接池統計資訊
- [ ] 確認連接重用率 > 90%
- [ ] 查詢回應時間改善

---

## ?? 常見問題與解決方案

### 問題 1: 編譯錯誤 - 找不到 ICrmConnectionPool

**解決方案**:
```csharp
// 確保添加了 using 語句
using ToolUtilityNameSpace.ConnectionOperations;
```

### 問題 2: 運行時錯誤 - 無法解析 ICrmConnectionPool

**解決方案**:
確認 `Startup.cs` 中已註冊連接池：
```csharp
services.AddSingleton<ICrmConnectionPool>(sp =>
{
    var connectionService = new CrmConnectionService();
    var serverUrl = "https://sunnyvalech.speechmessage.com.tw/XRMServices/2011/Organization.svc";
    var username = @"SPEECHMESSAGE\Administrator";
    var password = "hu9840";
    
    return new CrmConnectionPool(
        connectionService,
        serverUrl,
        username,
        password,
        minPoolSize: 3,
        maxPoolSize: 10,
        connectionTimeout: TimeSpan.FromSeconds(30),
        idleTimeout: TimeSpan.FromMinutes(10)
    );
});
```

### 問題 3: 連接池耗盡

**症狀**: TimeoutException - 無法在指定時間內取得連接

**可能原因**:
1. 忘記歸還連接（沒有調用 `ReleaseConnection()`）
2. 並發請求過多
3. 連接池太小

**解決方案**:
1. 檢查所有使用 `GetConnection()` 的地方是否有對應的 `ReleaseConnection()`
2. 使用 try-finally 確保連接被歸還：
```csharp
IOrganizationService service = null;
try
{
    service = GetConnection();
    // 使用連接
}
finally
{
    ReleaseConnection(service);
}
```
3. 增加連接池大小（在 Startup.cs 中調整 `maxPoolSize`）

---

## ?? 效能監控

### 監控連接池狀態

在任何 Controller 中添加監控端點：

```csharp
[HttpGet]
[Route("/api/connection-pool-stats")]
public IActionResult GetConnectionPoolStats()
{
    var stats = GetConnectionPoolStats();
    return Json(new
    {
        totalConnections = stats.TotalConnections,
        activeConnections = stats.ActiveConnections,
        idleConnections = stats.IdleConnections,
        waitingRequests = stats.WaitingRequests,
        totalAcquireCount = stats.TotalAcquireCount,
        totalReleaseCount = stats.TotalReleaseCount,
        timeoutCount = stats.TimeoutCount,
        validationFailureCount = stats.ValidationFailureCount,
        reuseRate = stats.TotalReleaseCount > 0 
            ? (double)(stats.TotalAcquireCount - stats.TotalConnections) / stats.TotalAcquireCount * 100 
            : 0
    });
}
```

### 監控指標解讀

| 指標 | 說明 | 理想值 |
|------|------|--------|
| `TotalConnections` | 連接池總連接數 | 3-10 |
| `ActiveConnections` | 使用中的連接數 | < `MaxPoolSize` |
| `IdleConnections` | 閒置的連接數 | > 0 |
| `WaitingRequests` | 等待連接的請求數 | 0 |
| `ReuseRate` | 連接重用率 | > 90% |
| `TimeoutCount` | 超時次數 | 0 |
| `ValidationFailureCount` | 驗證失敗次數 | < 5 |

---

## ?? 預期效果

### 效能提升
- ? 連接創建時間: ↓ 99% (500ms → 5ms)
- ? 查詢回應時間: ↓ 60-70% (3-5秒 → 1-1.5秒)
- ? 並發處理能力: ↑ 400% (20 req/s → 100+ req/s)
- ? CPU 使用率: ↓ 30%

### 資源管理
- ? 連接重用率 > 90%
- ? 記憶體使用穩定
- ? 無連接洩漏

---

## ?? 參考資料

- [Phase 1.2 完成報告](./Phase1.2-ConnectionPool-完成報告.md)
- [效能優化 TODO 清單](../效能優化TODO清單.md)
- [Object Pool Pattern](https://refactoring.guru/design-patterns/object-pool)

---

**文件版本**: v1.0  
**建立日期**: 2024-01-XX  
**最後更新**: 2024-01-XX  
**負責人**: 開發團隊
