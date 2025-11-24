# SetupUserLineIdRedirect 是否被正確調用 - 完整診斷

## ? 代碼檢查結果

### 1. HomeController.SetupUserLineIdRedirect 配置

**檔案**: `ChurchReport\Controllers\HomeController.cs`

```csharp
[HttpPost]
[Route("/Home/SetupUserLineId")]
public IActionResult SetupUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
{
    var dedicationController = new DedicationController(
        HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
        HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
        HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment);
    
    return dedicationController.SetupUserLineId(UserLineId, GroupId, RoomId, ViewType);
}
```

**狀態**: ? **配置正確**
- HTTP 方法: POST ?
- 路由: /Home/SetupUserLineId ?
- 參數: UserLineId, GroupId, RoomId, ViewType ?
- 轉發目標: DedicationController.SetupUserLineId ?

### 2. 視圖中的 AJAX 調用

**檔案**: `ChurchReport\Views\Home\DediationLineLoginView.cshtml` (第 286 行)

```javascript
$.ajax({
    url: '@Url.Action("SetupUserLineId", "Home")',  // 生成 /Home/SetupUserLineId
    data: { UserLineId: aUserLineId, GroupId: aGroupId, RoomId: aRoomId, ViewType: aViewType},
    type: 'POST',
    success: function (data) {
        window.location.href = "/Home/QPayView/" + aUserLineId;
    },
    error: function (obj) {
        getLoadPanelInstance().hide();
        window.location.href = "/Home/Login";
    }
});
```

**狀態**: ? **配置正確**
- AJAX URL: /Home/SetupUserLineId ?
- HTTP 方法: POST ?
- 參數名稱: 與後端一致 ?

### 3. Startup.cs 路由配置

**檔案**: `ChurchReport\Startup.cs`

```csharp
app.UseMvc(routes => 
{ 
    // 使用傳統 MVC 路由 + 屬性路由
    routes.MapRoute(
        name: "default", 
        template: "{controller=Authentication}/{action=Login}/{id?}");
});
```

**狀態**: ? **配置正確**
- 使用 `UseMvc` ?
- 支援屬性路由 (`[Route]`) ?
- `[HttpPost]` 屬性會被自動識別 ?

### 4. DedicationController.SetupUserLineId

**檔案**: `ChurchReport\Controllers\DedicationController.cs`

```csharp
[HttpPost]
public IActionResult SetupUserLineId(string UserLineId, string GroupId, string RoomId, string ViewType)
{
    try
    {
        InMemoryContext.LineBindingViewModel.LineUserId = UserLineId;
        InMemoryContext.LineBindingViewModel.RoomId = RoomId;
        InMemoryContext.LineBindingViewModel.GroupId = GroupId;
        InMemoryContext.LineBindingViewModel.ViewType = ViewType;
        
        // ... 其他邏輯
        
        return Json(new { status = "1" });
    }
    catch (Exception e)
    {
        return HandleError(e, "SetupUserLineId");
    }
}
```

**狀態**: ? **實作正確**

---

## ?? 如何確認是否被調用

### 方法 1: 瀏覽器開發者工具 (最直接)

```
1. 開啟 Chrome 並按 F12
2. 切換到 Network 標籤
3. 開啟 LIFF URL: 
   https://sunnyvalechback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
4. 完成 LINE 登入授權
5. 在 Network 中查找 SetupUserLineId
```

**預期結果**:
```
Request URL: https://sunnyvalechback.speechmessage.com.tw:479/Home/SetupUserLineId
Request Method: POST
Status Code: 200 OK
Response: {"status":"1"}
```

**如果看不到此請求**:
- ? JavaScript 錯誤阻止了 AJAX 調用
- ? LIFF 初始化失敗
- ? 用戶授權被拒絕

**如果看到 404**:
- ? 路由未正確註冊（但代碼檢查顯示配置正確）
- ? 應用程式池未運行
- ? IIS 配置問題

**如果看到 500**:
- ? 後端代碼執行錯誤
- ? 依賴注入失敗
- ? CRM 連線問題

### 方法 2: 添加 Console 日誌

修改視圖中的 JavaScript：

```javascript
function UpdateLineUserId(aUserLineId, aGroupId, aRoomId, aViewType) {
    console.log("=== 開始調用 SetupUserLineId ===");
    console.log("URL:", '@Url.Action("SetupUserLineId", "Home")');
    console.log("UserLineId:", aUserLineId);
    console.log("GroupId:", aGroupId);
    console.log("RoomId:", aRoomId);
    console.log("ViewType:", aViewType);
    
    $.ajax({
        url: '@Url.Action("SetupUserLineId", "Home")',
        data: { 
            UserLineId: aUserLineId, 
            GroupId: aGroupId, 
            RoomId: aRoomId, 
            ViewType: aViewType
        },
        type: 'POST',
        
        beforeSend: function() {
            console.log("=== AJAX 請求發送中 ===");
        },
        
        success: function (data) {
            console.log("=== AJAX Success ===");
            console.log("Response:", data);
            console.log("準備重導向到:", "/Home/QPayView/" + aUserLineId);
            window.location.href = "/Home/QPayView/" + aUserLineId;
        },
        
        error: function (xhr, status, error) {
            console.log("=== AJAX Error ===");
            console.log("Status:", status);
            console.log("Error:", error);
            console.log("Status Code:", xhr.status);
            console.log("Response Text:", xhr.responseText);
            
            getLoadPanelInstance().hide();
            window.location.href = "/Home/Login";
        }
    });
}
```

**檢查 Console 輸出**:
- 如果看到 "開始調用 SetupUserLineId" → UpdateLineUserId 被調用 ?
- 如果看到 "AJAX 請求發送中" → AJAX 開始執行 ?
- 如果看到 "AJAX Success" → 後端調用成功 ?
- 如果看到 "AJAX Error" → 後端調用失敗 ?

### 方法 3: 後端日誌追蹤

修改 `HomeController.SetupUserLineIdRedirect`：

```csharp
[HttpPost]
[Route("/Home/SetupUserLineId")]
public IActionResult SetupUserLineIdRedirect(string UserLineId, string GroupId, string RoomId, string ViewType)
{
    // 寫入日誌
    ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, 
        $"[SetupUserLineIdRedirect] 開始執行 - UserLineId: {UserLineId}");
    
    try
    {
        // 記錄接收到的參數
        ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, 
            $"[SetupUserLineIdRedirect] 參數 - GroupId: {GroupId}, RoomId: {RoomId}, ViewType: {ViewType}");
        
        // 創建 DedicationController
        ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, 
            "[SetupUserLineIdRedirect] 準備創建 DedicationController");
        
        var dedicationController = new DedicationController(
            HttpContext.RequestServices.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor,
            HttpContext.RequestServices.GetService(typeof(IMemoryCache)) as IMemoryCache,
            HttpContext.RequestServices.GetService(typeof(IPayment)) as IPayment);
        
        ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, 
            "[SetupUserLineIdRedirect] DedicationController 創建成功，準備調用 SetupUserLineId");
        
        var result = dedicationController.SetupUserLineId(UserLineId, GroupId, RoomId, ViewType);
        
        ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, 
            "[SetupUserLineIdRedirect] SetupUserLineId 調用成功");
        
        return result;
    }
    catch (Exception ex)
    {
        ToolUtility.TraceByLevel(TOTAL_LEVEL, LEVEL_1, 
            $"[SetupUserLineIdRedirect] 錯誤: {ex.Message}\nStackTrace: {ex.StackTrace}");
        
        return HandleError(ex, "SetupUserLineIdRedirect");
    }
}
```

**檢查日誌**:
```powershell
# 查看 Trace.log
Get-Content "ChurchReport\Logs\Trace.log" -Tail 50 | Where-Object { $_ -like "*SetupUserLineIdRedirect*" }
```

**預期日誌**:
```
[時間] [SetupUserLineIdRedirect] 開始執行 - UserLineId: U7638e4ed509708a3573ba6d69970583d
[時間] [SetupUserLineIdRedirect] 參數 - GroupId: , RoomId: , ViewType: 
[時間] [SetupUserLineIdRedirect] 準備創建 DedicationController
[時間] [SetupUserLineIdRedirect] DedicationController 創建成功，準備調用 SetupUserLineId
[時間] [SetupUserLineIdRedirect] SetupUserLineId 調用成功
```

**如果沒有任何日誌**:
- ? `SetupUserLineIdRedirect` 根本沒有被調用
- 可能原因: 路由問題、IIS 問題、AJAX 未發送請求

---

## ?? 完整的調用鏈診斷

```
用戶開啟 LIFF URL
    ↓
[檢查點 1] DedicationController.DediationLineLoginView
    ├─ TempData["Proponent"] 是否設定？
    └─ 視圖是否正確返回？
    ↓
[檢查點 2] 視圖載入
    ├─ LIFF SDK 是否載入？
    ├─ JavaScript 是否有錯誤？
    └─ LoadPanel 是否正確定義？
    ↓
[檢查點 3] LIFF 初始化
    ├─ liff.init() 是否成功？
    ├─ liff.isLoggedIn() 返回 true？
    └─ 權限檢查是否通過？
    ↓
[檢查點 4] 取得使用者 Profile
    ├─ liff.getProfile() 是否成功？
    ├─ UserId 是否正確取得？
    └─ DisplayName 是否顯示？
    ↓
[檢查點 5] 調用 UpdateLineUserId
    ├─ 函數是否被執行？
    ├─ 參數是否正確？
    └─ AJAX URL 是否正確？
    ↓
[檢查點 6] AJAX 請求發送
    ├─ Network 中是否看到請求？
    ├─ Request URL 是否正確？
    ├─ Request Method 是否為 POST？
    └─ Request Payload 是否包含參數？
    ↓
[檢查點 7] ?? HomeController.SetupUserLineIdRedirect
    ├─ 方法是否被調用？ ← **這裡是關鍵**
    ├─ 參數是否正確接收？
    └─ 是否有異常拋出？
    ↓
[檢查點 8] 創建 DedicationController
    ├─ 依賴注入是否成功？
    ├─ IHttpContextAccessor 是否可用？
    └─ IMemoryCache 是否可用？
    ↓
[檢查點 9] DedicationController.SetupUserLineId
    ├─ InMemoryContext 是否正常？
    ├─ ToolUtility.RetrieveContactByLineId 是否成功？
    └─ 返回 JSON {"status":"1"} ？
    ↓
[檢查點 10] AJAX Success 回調
    ├─ success 函數是否執行？
    ├─ data.status 是否為 "1"？
    └─ 是否執行重導向？
    ↓
? 重導向到 /Home/QPayView/{LineUserId}
```

---

## ?? 實際測試步驟

### 步驟 1: 準備測試環境

```powershell
# 1. 確認應用程式池正在運行
Import-Module WebAdministration
Get-WebAppPoolState "ChurchReport"
# 預期: Started

# 2. 確認 IIS 服務運行
sc query W3SVC
# 預期: STATE = RUNNING

# 3. 重啟應用程式池 (清除狀態)
Restart-WebAppPool "ChurchReport"
```

### 步驟 2: 準備瀏覽器

```
1. 開啟 Chrome
2. 按 F12 開啟 DevTools
3. 切換到 Console 標籤 (查看 JavaScript 日誌)
4. 切換到 Network 標籤 (查看網路請求)
5. 勾選 "Preserve log" (保留日誌)
```

### 步驟 3: 執行測試

```
1. 在 LINE 應用程式中開啟 LIFF URL:
   https://sunnyvalechback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy

2. 完成 LINE 登入授權

3. 觀察 Console 輸出

4. 觀察 Network 標籤中的請求
```

### 步驟 4: 分析結果

#### 場景 A: 成功 ?
```
Console 輸出:
- "開始調用 SetupUserLineId"
- "AJAX 請求發送中"
- "AJAX Success"
- "準備重導向到: /Home/QPayView/U..."

Network 輸出:
- SetupUserLineId: 200 OK
- Response: {"status":"1"}
- 頁面重導向到 QPayView
```

**結論**: `SetupUserLineIdRedirect` **被正確調用** ?

#### 場景 B: AJAX 404 錯誤 ?
```
Console 輸出:
- "開始調用 SetupUserLineId"
- "AJAX 請求發送中"
- "AJAX Error"
- "Status Code: 404"

Network 輸出:
- SetupUserLineId: 404 Not Found
```

**結論**: `SetupUserLineIdRedirect` **未被調用** ?
**原因**: 路由問題或應用程式未正確部署

**修復**:
1. 確認編譯成功並部署最新代碼
2. 重啟 IIS: `iisreset /restart`
3. 檢查 Startup.cs 中的路由配置

#### 場景 C: AJAX 500 錯誤 ?
```
Console 輸出:
- "開始調用 SetupUserLineId"
- "AJAX 請求發送中"
- "AJAX Error"
- "Status Code: 500"

Network 輸出:
- SetupUserLineId: 500 Internal Server Error
- Response: (錯誤訊息)
```

**結論**: `SetupUserLineIdRedirect` **被調用** ?，但執行時出錯 ?
**原因**: 後端代碼執行錯誤

**修復**:
1. 查看 `Logs\Trace.log` 中的錯誤訊息
2. 檢查依賴注入是否成功
3. 檢查 CRM 連線

#### 場景 D: AJAX 未發送 ?
```
Console 輸出:
- (可能有 JavaScript 錯誤)
- 沒有 "開始調用 SetupUserLineId"

Network 輸出:
- 沒有 SetupUserLineId 請求
```

**結論**: `UpdateLineUserId` 函數未被調用 ?
**原因**: JavaScript 錯誤或 LIFF 初始化失敗

**修復**:
1. 檢查 Console 中的 JavaScript 錯誤
2. 確認 LIFF SDK 正確載入
3. 確認 LoadPanel 定義正確

---

## ?? 快速診斷檢查清單

### 前端檢查
- [ ] LIFF SDK 正確載入
- [ ] LIFF 初始化成功 (liff.init)
- [ ] 用戶已登入 (liff.isLoggedIn)
- [ ] 權限授權成功 (profile scope)
- [ ] liff.getProfile() 成功取得資料
- [ ] UpdateLineUserId 函數被調用
- [ ] AJAX URL 正確 (/Home/SetupUserLineId)
- [ ] AJAX 請求發送成功

### 後端檢查
- [ ] HomeController 包含 SetupUserLineIdRedirect 方法
- [ ] [HttpPost] 屬性存在
- [ ] [Route("/Home/SetupUserLineId")] 屬性存在
- [ ] 參數名稱與 AJAX 一致
- [ ] Startup.cs 使用 UseMvc
- [ ] 應用程式編譯成功
- [ ] IIS 應用程式池運行中
- [ ] 日誌中有調用記錄

### 網路檢查
- [ ] Network 中看到 SetupUserLineId 請求
- [ ] Status Code 為 200 OK
- [ ] Response 為 {"status":"1"}
- [ ] 沒有 CORS 錯誤
- [ ] 沒有 SSL 證書錯誤

---

## ?? 結論

根據代碼檢查，**所有配置都是正確的**：

1. ? `HomeController.SetupUserLineIdRedirect` 方法存在且配置正確
2. ? 路由屬性 `[Route("/Home/SetupUserLineId")]` 正確
3. ? HTTP 方法 `[HttpPost]` 正確
4. ? 視圖中的 AJAX URL 正確
5. ? Startup.cs 支援屬性路由

**如果實際運行時失敗，請使用上述測試方法來診斷**：

### 最直接的診斷方法
1. **開啟 Chrome DevTools → Network 標籤**
2. **在 LINE 中開啟 LIFF URL**
3. **查找 SetupUserLineId 請求**
4. **檢查 Status Code 和 Response**

如果：
- **看到 200 OK** → `SetupUserLineIdRedirect` **被正確調用** ?
- **看到 404** → 路由問題，需要檢查部署 ?
- **看到 500** → 後端執行錯誤，需要查看日誌 ?
- **沒看到請求** → 前端 JavaScript 問題 ?

---

**建議**: 請執行上述的實際測試步驟，並回報 Network 標籤中看到的結果，這樣可以準確判斷問題所在。
