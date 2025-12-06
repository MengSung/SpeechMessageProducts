# SetupUserLineIdRedirect 調用診斷報告

## ?? 問題分析

### 視圖中的 AJAX 調用
**檔案**: `ChurchReport\Views\Home\DediationLineLoginView.cshtml` (第 286 行)

```javascript
url: '@Url.Action("SetupUserLineId", "Home")',
```

### HomeController 中的路由定義
**檔案**: `ChurchReport\Controllers\HomeController.cs`

```csharp
[HttpPost]
[Route("/Home/SetupUserLineId")]
public IActionResult SetupUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
{
    // 直接調用 DedicationController 的方法
    var dedicationController = new DedicationController(
        HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
        HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
        HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment);
    
    return dedicationController.SetupUserLineId(UserLineId, GroupId, RoomId, ViewType);
}
```

## ? 路由配置狀態

| 項目 | 狀態 | 說明 |
|------|------|------|
| AJAX URL | ? 正確 | `/Home/SetupUserLineId` |
| HomeController 路由 | ? 存在 | `[Route("/Home/SetupUserLineId")]` |
| HTTP 方法 | ? 一致 | POST |
| 參數 | ? 一致 | UserLineId, GroupId, RoomId, ViewType |
| 轉發目標 | ? 正確 | DedicationController.SetupUserLineId |

## ?? 測試方法

### 方法 1: 瀏覽器 Network 監控

```
1. 開啟 Chrome DevTools (F12)
2. 切換到 Network 標籤
3. 開啟 LIFF URL: 
   https://jesusback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
4. 完成 LINE 登入授權
5. 查找 SetupUserLineId 請求
```

**預期結果**:
```
Request URL: https://jesusback.speechmessage.com.tw:479/Home/SetupUserLineId
Request Method: POST
Status Code: 200 OK
Response: {"status":"1"}
```

**如果失敗**:
- Status Code 404: 路由未正確註冊
- Status Code 500: 後端處理錯誤
- Timeout: CRM 連線問題

### 方法 2: 添加 Console 日誌

在視圖中添加日誌輸出：

```javascript
function UpdateLineUserId(aUserLineId, aGroupId, aRoomId, aViewType) {
    console.log("=== UpdateLineUserId 開始 ===");
    console.log("UserLineId:", aUserLineId);
    console.log("GroupId:", aGroupId);
    console.log("RoomId:", aRoomId);
    console.log("ViewType:", aViewType);
    console.log("AJAX URL:", '@Url.Action("SetupUserLineId", "Home")');
    
    $.ajax({
        url: '@Url.Action("SetupUserLineId", "Home")',
        data: { 
            UserLineId: aUserLineId, 
            GroupId: aGroupId, 
            RoomId: aRoomId, 
            ViewType: aViewType
        },
        type: 'POST',
        
        success: function (data) {
            console.log("=== AJAX Success ===");
            console.log("Response:", data);
            window.location.href = "/Home/QPayView/" + aUserLineId;
        },
        
        error: function (xhr, status, error) {
            console.log("=== AJAX Error ===");
            console.log("Status:", status);
            console.log("Error:", error);
            console.log("Response Text:", xhr.responseText);
            console.log("Status Code:", xhr.status);
            
            getLoadPanelInstance().hide();
            window.location.href = "/Home/Login";
        }
    });
}
```

### 方法 3: 後端日誌檢查

在 `SetupUserLineIdRedirect` 方法開頭添加日誌：

```csharp
[HttpPost]
[Route("/Home/SetupUserLineId")]
public IActionResult SetupUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
{
    // 添加日誌
    ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, 
        $"SetupUserLineIdRedirect 被調用 - UserLineId: {UserLineId}, GroupId: {GroupId}, RoomId: {RoomId}, ViewType: {ViewType}");
    
    try
    {
        var dedicationController = new DedicationController(
            HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
            HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
            HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment);
        
        ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "準備調用 DedicationController.SetupUserLineId");
        
        var result = dedicationController.SetupUserLineId(UserLineId, GroupId, RoomId, ViewType);
        
        ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "DedicationController.SetupUserLineId 調用成功");
        
        return result;
    }
    catch (Exception ex)
    {
        ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"SetupUserLineIdRedirect 錯誤: {ex.Message}");
        return HandleError(ex, "SetupUserLineIdRedirect");
    }
}
```

## ?? 診斷流程圖

```
用戶開啟 LIFF URL
    ↓
DedicationController.DediationLineLoginView
    ├─ TempData["Proponent"] = LIFF ID
    └─ 返回視圖
    ↓
LIFF 初始化
    ├─ liff.init()
    └─ 檢查登入狀態
    ↓
取得使用者 Profile
    ├─ liff.getProfile()
    └─ UserId, DisplayName
    ↓
【關鍵點】UpdateLineUserId() 被調用
    ↓
AJAX POST → /Home/SetupUserLineId
    ↓
【檢查點 1】HomeController.SetupUserLineIdRedirect
    ├─ 是否被執行？
    ├─ 參數是否正確接收？
    └─ 是否有錯誤拋出？
    ↓
【檢查點 2】創建 DedicationController 實例
    ├─ 依賴注入是否成功？
    └─ HttpContextAccessor 是否有效？
    ↓
【檢查點 3】DedicationController.SetupUserLineId
    ├─ 是否被調用？
    ├─ InMemoryContext 是否正常？
    ├─ ToolUtility.RetrieveContactByLineId 是否成功？
    └─ 返回 JSON {"status":"1"} ？
    ↓
【檢查點 4】AJAX Success 回調
    ├─ 是否接收到 data？
    └─ 是否執行重導向？
    ↓
? 重導向到 /Home/QPayView/{LineUserId}
```

## ?? 可能的失敗點

### 失敗點 1: SetupUserLineIdRedirect 未被調用

**症狀**:
- Network 中看到 404 錯誤
- 或沒有看到 `/Home/SetupUserLineId` 請求

**原因**:
- 路由未正確註冊
- IIS URL Rewrite 攔截
- 應用程式池未運行

**檢查**:
```powershell
# 檢查 Startup.cs 中的路由配置
Get-Content "ChurchReport\Startup.cs" | Select-String "MapControllers"

# 檢查應用程式池
Import-Module WebAdministration
Get-WebAppPoolState "ChurchReport"
```

### 失敗點 2: DedicationController 實例化失敗

**症狀**:
- 500 錯誤
- 日誌中有 "Object reference not set" 錯誤

**原因**:
- 依賴注入服務獲取失敗
- IHttpContextAccessor 未註冊

**修復**:
```csharp
// 檢查 Startup.cs 中是否有註冊
services.AddHttpContextAccessor();
```

### 失敗點 3: InMemoryContext 狀態問題

**症狀**:
- SetupUserLineId 執行但沒有效果
- QPayView 顯示空白或錯誤資料

**原因**:
- InMemoryContext 被其他請求覆蓋（共用實例問題）
- Session 過期

**診斷**:
```csharp
// 在 SetupUserLineId 中添加
Console.WriteLine($"LineBindingViewModel.LineUserId: {InMemoryContext.LineBindingViewModel.LineUserId}");
Console.WriteLine($"QpayManager.LoginType: {InMemoryContext.QpayManager.LoginType}");
```

### 失敗點 4: CRM 連線失敗

**症狀**:
- SetupUserLineId 執行很慢或超時
- 日誌中有 CRM 連線錯誤

**原因**:
- CRM 服務離線
- 網路連線問題
- 認證失敗

**檢查**:
```csharp
// 測試 CRM 連線
var loginContact = ToolUtility.RetrieveContactByLineId(UserLineId);
if (loginContact == null) {
    ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"找不到 LINE ID: {UserLineId}");
}
```

## ?? 快速診斷腳本

```powershell
# PowerShell 腳本 - 診斷SetupUserLineIdRedirect.ps1

Write-Host "=== SetupUserLineIdRedirect 診斷 ===" -ForegroundColor Cyan

# 1. 檢查方法是否存在
Write-Host "`n[1/5] 檢查 HomeController 中的方法..." -ForegroundColor Yellow
$homeController = Get-Content "ChurchReport\Controllers\HomeController.cs" -Raw
if ($homeController -like "*SetupUserLineIdRedirect*") {
    Write-Host "? SetupUserLineIdRedirect 方法存在" -ForegroundColor Green
} else {
    Write-Host "? SetupUserLineIdRedirect 方法不存在" -ForegroundColor Red
}

# 2. 檢查路由屬性
Write-Host "`n[2/5] 檢查路由屬性..." -ForegroundColor Yellow
if ($homeController -like '*[Route("/Home/SetupUserLineId")]*') {
    Write-Host "? 路由屬性正確設定" -ForegroundColor Green
} else {
    Write-Host "? 路由屬性缺失或錯誤" -ForegroundColor Red
}

# 3. 檢查 HTTP 方法
Write-Host "`n[3/5] 檢查 HTTP 方法屬性..." -ForegroundColor Yellow
if ($homeController -like "*[HttpPost]*SetupUserLineIdRedirect*") {
    Write-Host "? [HttpPost] 屬性正確設定" -ForegroundColor Green
} else {
    Write-Host "? [HttpPost] 屬性缺失" -ForegroundColor Red
}

# 4. 檢查視圖中的 AJAX 調用
Write-Host "`n[4/5] 檢查視圖中的 AJAX URL..." -ForegroundColor Yellow
$view = Get-Content "ChurchReport\Views\Home\DediationLineLoginView.cshtml" -Raw
if ($view -like '*Url.Action("SetupUserLineId", "Home")*') {
    Write-Host "? 視圖 AJAX URL 正確" -ForegroundColor Green
} else {
    Write-Host "? 視圖 AJAX URL 錯誤或缺失" -ForegroundColor Red
}

# 5. 檢查應用程式池狀態
Write-Host "`n[5/5] 檢查 IIS 應用程式池..." -ForegroundColor Yellow
try {
    Import-Module WebAdministration -ErrorAction Stop
    $poolState = (Get-WebAppPoolState "ChurchReport" -ErrorAction Stop).Value
    if ($poolState -eq "Started") {
        Write-Host "? 應用程式池正在運行" -ForegroundColor Green
    } else {
        Write-Host "?? 應用程式池狀態: $poolState" -ForegroundColor Yellow
    }
} catch {
    Write-Host "?? 無法檢查應用程式池狀態" -ForegroundColor Yellow
}

Write-Host "`n=== 診斷完成 ===" -ForegroundColor Cyan
Write-Host "`n建議的測試步驟:"
Write-Host "1. 在瀏覽器中開啟 DevTools (F12)"
Write-Host "2. 切換到 Network 標籤"
Write-Host "3. 在 LINE 中開啟 LIFF URL"
Write-Host "4. 查找 SetupUserLineId 請求"
Write-Host "5. 檢查 Status Code 和 Response"
```

## ?? 測試用例

### 測試用例 1: 正常流程
```
輸入:
- UserLineId: "U7638e4ed509708a3573ba6d69970583d"
- GroupId: ""
- RoomId: ""
- ViewType: ""

預期:
- SetupUserLineIdRedirect 被調用
- DedicationController.SetupUserLineId 被調用
- 返回 {"status":"1"}
- 重導向到 /Home/QPayView/U7638e4ed509708a3573ba6d69970583d
```

### 測試用例 2: 群組聊天
```
輸入:
- UserLineId: "U7638e4ed509708a3573ba6d69970583d"
- GroupId: "C1234567890abcdef"
- RoomId: ""
- ViewType: "group"

預期:
- DisplayId 設為 GroupId
- 正常返回並重導向
```

### 測試用例 3: 未綁定用戶
```
輸入:
- UserLineId: "Uunknown123" (不存在於 CRM)

預期:
- RetrieveContactByLineId 返回 null
- 仍然返回 {"status":"1"}
- 但 QpayManager 未設定用戶資料
```

## ?? 如何確認是否被調用

### 方法 A: 添加日誌檔案寫入

```csharp
[HttpPost]
[Route("/Home/SetupUserLineId")]
public IActionResult SetupUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
{
    // 寫入簡單的文字日誌
    System.IO.File.AppendAllText(
        "Logs/SetupUserLineIdRedirect.log", 
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Called with UserLineId: {UserLineId}\n");
    
    // ... 其餘代碼
}
```

### 方法 B: 使用 Application Insights (如果已配置)

```csharp
TelemetryClient telemetry = new TelemetryClient();
telemetry.TrackEvent("SetupUserLineIdRedirect Called", 
    new Dictionary<string, string> {
        { "UserLineId", UserLineId },
        { "GroupId", GroupId }
    });
```

### 方法 C: 檢查 IIS 日誌

```powershell
# 查看 IIS 日誌中的 POST 請求
Get-Content "C:\inetpub\logs\LogFiles\W3SVC*\*.log" -Tail 100 | 
    Where-Object { $_ -like "*SetupUserLineId*" } |
    ForEach-Object {
        Write-Host $_ -ForegroundColor Cyan
    }
```

## ? 確認清單

執行以下檢查來確認 `SetupUserLineIdRedirect` 是否被正確調用：

- [ ] 方法存在於 HomeController.cs
- [ ] 有 [HttpPost] 屬性
- [ ] 有 [Route("/Home/SetupUserLineId")] 屬性
- [ ] 參數名稱與視圖 AJAX 一致
- [ ] 視圖中的 AJAX URL 指向 /Home/SetupUserLineId
- [ ] 應用程式編譯成功
- [ ] IIS 應用程式池運行中
- [ ] 瀏覽器 Network 顯示請求成功 (200 OK)
- [ ] 後端日誌中有調用記錄
- [ ] DedicationController.SetupUserLineId 被執行
- [ ] 返回正確的 JSON {"status":"1"}
- [ ] 成功重導向到 QPayView

---

**結論**: 根據代碼檢查，`SetupUserLineIdRedirect` 的配置是**正確**的。如果實際運行時仍然失敗，問題可能出在：

1. **路由註冊**: 確認 Startup.cs 中有 `endpoints.MapControllers()`
2. **應用程式狀態**: 確認 IIS 應用程式池正在運行
3. **依賴注入**: 確認 IHttpContextAccessor 已註冊
4. **實例化問題**: DedicationController 的手動實例化可能導致 InMemoryContext 不共用

**建議**: 使用上述的測試方法（特別是瀏覽器 Network 監控和後端日誌）來確認實際的調用情況。
