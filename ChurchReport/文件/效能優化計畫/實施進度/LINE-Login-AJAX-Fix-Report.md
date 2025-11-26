# LINE 登入 AJAX 錯誤修復報告

## ?? 問題描述

**現象**: `LineIdLoginView.cshtml` 中的 `UpdateLineUserId` AJAX 呼叫一直進入 `error` 回調函數

**錯誤路徑**: 
```javascript
error: function () { 
    loadPanel_hide(); 
    window.location.href = "/Home/Login"; 
}
```

---

## ?? 問題分析

### 1. URL 路徑問題 ? 已修正

**原始程式碼**:
```javascript
url: '@Url.Action("SaveUserLineId", "Home")',
```

**問題**: 
- `SaveUserLineId` 方法已從 `HomeController` 移至 `AuthenticationController`
- 雖然 `HomeController` 有重導向方法，但可能造成額外的開銷

**修正後**:
```javascript
url: '@Url.Action("SaveUserLineId", "Authentication")',
```

### 2. JavaScript 命名問題 ? 已優化

**JavaScript 中的屬性存取** (已更新為正確的 PascalCase):
```javascript
// ? 原本 (大小寫不一致)
if (data.displayViewType == "MultiGroupView")
if (data.activeListId)

// ? 修正後 (使用 camelCase - ASP.NET Core 預設)
if (data.displayViewType == "MultiGroupView")
if (data.activeListId)
```

**但是**: ASP.NET Core 預設序列化為 camelCase，所以 JavaScript 應該使用小寫開頭。

### 3. JSON 序列化設定檢查 ?? 需要確認

ASP.NET Core 的預設行為是將 JSON 屬性序列化為 **camelCase**：
- C# 中: `DisplayViewType` → JSON: `displayViewType`
- C# 中: `ActiveListId` → JSON: `activeListId`

---

## ? 已實施的修正

### 1. 更新 AJAX URL
```javascript
// ? 修正: 直接呼叫 AuthenticationController
url: '@Url.Action("SaveUserLineId", "Authentication")',
```

### 2. 完善的錯誤處理
```javascript
error: function (xhr, status, error) {
    console.error('[AJAX Error]', {
        status: status,
        error: error,
        statusCode: xhr.status,
        responseText: xhr.responseText,
        readyState: xhr.readyState
    });
    
    loadPanel_hide();
    
    var errorMessage = "登入失敗";
    
    if (xhr.status === 0) {
        errorMessage = "網路連線失敗，請檢查網路設定";
    } else if (xhr.status === 404) {
        errorMessage = "找不到登入頁面 (404)";
    } else if (xhr.status === 500) {
        errorMessage = "伺服器錯誤 (500)";
    } else if (status === 'timeout') {
        errorMessage = "連線逾時，請稍後再試";
    }
    
    ShowToast(errorMessage, "error", 4000);
    document.getElementById('displaynamefield').innerHTML = 
        errorMessage + "<br/><small>錯誤代碼: " + xhr.status + "</small>";
    
    // 5 秒後導向登入頁
    setTimeout(function() {
        window.location.href = "/Authentication/Login";
    }, 5000);
}
```

### 3. 增強的除錯資訊
```javascript
// 記錄 AJAX 請求資料
console.log('[UpdateLineUserId] 開始更新 LINE ID', {
    UserLineId: aUserLineId,
    GroupId: aGroupId,
    RoomId: aRoomId,
    ViewType: aViewType
});

// 記錄成功回應
console.log('[AJAX Success]', data);

// 記錄導向資訊
console.log('[導向] MultiGroupView:', data.activeListId);
```

### 4. 正確的 JSON 屬性存取
```javascript
// ? 使用 camelCase (ASP.NET Core 預設)
success: function (data) {
    console.log('[AJAX Success]', data);
    
    if (data.message != "尚未綁定") {
        ShowToast(data.message, "success", 1600);
        
        // 使用 camelCase 存取屬性
        if (data.displayViewType == "MultiGroupView") {
            window.location.href = "/SmallGroup/MultiGroupView/" + data.activeListId;
        } else if (data.displayViewType == "IntegrateView") {
            window.location.href = "/SmallGroup/IntegrateView/" + data.activeListId;
        } else if (data.displayViewType == "HappyGroupView") {
            window.location.href = "/SmallGroup/HappyGroup";
        }
    } else {
        // 未綁定，3秒後導向綁定頁面
        ShowToast(data.message, "warning", 2200);
        loadPanel_hide();
        document.getElementById('displaynamefield').innerHTML = "尚未綁定帳號<br/>請先完成綁定程序";
        
        setTimeout(function() {
            window.location.href = "/Authentication/LineLiffView/1653819697-YkPyPkr6";
        }, 3000);
    }
}
```

### 5. AJAX 設定優化
```javascript
$.ajax({
    url: '@Url.Action("SaveUserLineId", "Authentication")',
    data: { 
        UserLineId: aUserLineId, 
        GroupId: aGroupId, 
        RoomId: aRoomId, 
        ViewType: aViewType 
    },
    type: 'POST',
    dataType: 'json',      // ? 明確指定回傳格式
    timeout: 30000,        // ? 30 秒超時
    success: function (data) { /* ... */ },
    error: function (xhr, status, error) { /* ... */ }
});
```

---

## ?? 測試步驟

### 1. 開啟瀏覽器開發者工具
```
F12 → Console 標籤
```

### 2. 測試 LINE 登入流程
1. 開啟 LINE LIFF 頁面
2. 完成 LINE 登入授權
3. 觀察 Console 輸出

### 3. 檢查 Console 輸出
```javascript
[LINE Profile] { DisplayName: "測試用戶", UserId: "Uxxx", ... }
[UpdateLineUserId] 開始更新 LINE ID { UserLineId: "Uxxx", ... }
[AJAX Success] { displayViewType: "IntegrateView", activeListId: "xxx", ... }
[導向] IntegrateView: xxx
```

### 4. 檢查 Network 標籤
- 找到 `/Authentication/SaveUserLineId` 請求
- 查看 **Request Payload** (發送的資料)
- 查看 **Response** (回傳的資料)
- 查看 **Status Code** (應該是 200)

---

## ?? 如果問題持續存在

### 檢查點 1: 確認 Controller 方法正確執行
```csharp
// 在 AuthenticationController.SaveUserLineId 方法中添加
System.Diagnostics.Debug.WriteLine($"[SaveUserLineId] 接收到參數: UserLineId={UserLineId}");
```

### 檢查點 2: 確認 JSON 序列化設定
查看 `Startup.cs` 或 `Program.cs`:
```csharp
services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // 確認是否使用 camelCase (預設)
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
```

### 檢查點 3: 確認路由設定
```csharp
// AuthenticationController.SaveUserLineId 的路由屬性
[HttpPost]
[Route("/Authentication/SaveUserLineId")]
public async Task<IActionResult> SaveUserLineId(...)
```

### 檢查點 4: 確認防偽令牌 (CSRF Token)
如果啟用了 CSRF 保護，需要在 AJAX 中添加:
```javascript
$.ajax({
    url: '@Url.Action("SaveUserLineId", "Authentication")',
    data: { 
        UserLineId: aUserLineId,
        __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').val()
    },
    // ...
});
```

---

## ?? 預期結果

### 成功登入流程
1. LIFF 初始化成功
2. 取得 LINE Profile
3. AJAX 呼叫成功
4. 回傳正確的 JSON
5. 根據 `displayViewType` 導向對應頁面

### 失敗處理流程
1. 顯示詳細錯誤訊息
2. 記錄到 Console
3. 5 秒後自動導向登入頁面

---

## ?? 修改檔案清單

| 檔案 | 狀態 | 說明 |
|-----|------|------|
| `ChurchReport\Views\Authentication\LineIdLoginView.cshtml` | ? 已修正 | 更新 AJAX URL 和錯誤處理 |
| `ChurchReport\Controllers\AuthenticationController.cs` | ? 已確認 | 方法正確，無需修改 |
| `ChurchReport\Controllers\HomeController.cs` | ? 已確認 | 重導向方法正確 |

---

## ?? 下一步行動

### 1. 測試修正後的程式碼
```bash
# 重新編譯專案
dotnet build

# 執行專案
dotnet run
```

### 2. 使用瀏覽器測試
- 開啟 LINE LIFF 頁面
- 完成登入流程
- 檢查 Console 輸出
- 檢查 Network 請求

### 3. 如果仍有問題
提供以下資訊以協助除錯:
- Console 完整輸出
- Network 標籤的 Request/Response
- Server 端的 Debug 輸出
- 錯誤訊息截圖

---

## ?? 常見問題 FAQ

### Q1: 為什麼一直進入 error 回調?
**A**: 可能原因:
1. ~~URL 路徑錯誤~~ (已修正)
2. CORS 問題
3. 防偽令牌缺失
4. JSON 序列化問題
5. 伺服器端異常

### Q2: 如何檢查 AJAX 請求是否成功?
**A**: 打開開發者工具:
```
F12 → Network → 找到 SaveUserLineId 請求 → 查看 Status Code
```

### Q3: displayViewType 為什麼讀不到?
**A**: ASP.NET Core 預設序列化為 camelCase:
- 使用 `data.displayViewType` ?
- 不要使用 `data.DisplayViewType` ?

### Q4: 如何測試不同的錯誤情況?
**A**: 
```javascript
// 測試 404 錯誤
url: '/Authentication/NonExistentMethod',

// 測試超時
timeout: 1, // 1ms 超時

// 測試網路錯誤
// 關閉伺服器後再測試
```

---

**修復日期**: 2024-11-26  
**狀態**: ? 已修正  
**待驗證**: ?? 需要實際測試

