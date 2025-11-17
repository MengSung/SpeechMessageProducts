# UpdateLineUserId 沒有呼叫到 SetupUserLineId 的問題診斷

## ?? 問題描述

**症狀**: `UpdateLineUserId` 函數沒有成功呼叫到 `@Url.Action("SetupUserLineId", "Home")`

**位置**: `ChurchReport\Views\Home\DediationLineLoginView.cshtml` 第 286 行

```javascript
url: '@Url.Action("SetupUserLineId", "Home")',
```

---

## ?? 診斷步驟

### 步驟 1: 檢查 UpdateLineUserId 是否被執行

在視圖中添加 Console 日誌：

```javascript
function UpdateLineUserId(aUserLineId, aGroupId, aRoomId, aViewType) {
    console.log("=== UpdateLineUserId 被調用 ===");
    console.log("UserLineId:", aUserLineId);
    console.log("GroupId:", aGroupId);
    console.log("RoomId:", aRoomId);
    console.log("ViewType:", aViewType);
    
    // 檢查 URL 是否正確生成
    var ajaxUrl = '@Url.Action("SetupUserLineId", "Home")';
    console.log("AJAX URL:", ajaxUrl);
    console.log("AJAX URL Type:", typeof ajaxUrl);
    console.log("AJAX URL Length:", ajaxUrl.length);
    
    // 檢查 jQuery 是否載入
    if (typeof $ === 'undefined') {
        console.error("? jQuery 未載入！");
        document.getElementById('displaynamefield').innerHTML = "錯誤：jQuery 未載入";
        return;
    } else {
        console.log("? jQuery 已載入，版本:", $.fn.jquery);
    }
    
    console.log("準備發送 AJAX 請求...");
    
    // AJAX 區塊
    $.ajax({
        url: ajaxUrl,
        data: { 
            UserLineId: aUserLineId, 
            GroupId: aGroupId, 
            RoomId: aRoomId, 
            ViewType: aViewType
        },
        type: 'POST',
        
        beforeSend: function(xhr, settings) {
            console.log("=== beforeSend 觸發 ===");
            console.log("URL:", settings.url);
            console.log("Type:", settings.type);
            console.log("Data:", settings.data);
        },
        
        success: function (data) {
            console.log("=== AJAX Success ===");
            console.log("Response:", data);
            console.log("Response Type:", typeof data);
            
            if (data && data.status) {
                console.log("Status:", data.status);
            }
            
            console.log("準備重導向到:", "/Home/QPayView/" + aUserLineId);
            window.location.href = "/Home/QPayView/" + aUserLineId;
        },
        
        error: function (xhr, status, error) {
            console.log("=== AJAX Error ===");
            console.error("Status:", status);
            console.error("Error:", error);
            console.error("Status Code:", xhr.status);
            console.error("Status Text:", xhr.statusText);
            console.error("Response Text:", xhr.responseText);
            console.error("Response Headers:", xhr.getAllResponseHeaders());
            
            // 顯示錯誤訊息
            document.getElementById('displaynamefield').innerHTML = 
                "AJAX 錯誤：" + status + " - " + error + "<br>Status Code: " + xhr.status;
            
            getLoadPanelInstance().hide();
            
            // 暫時不要自動重導向，先看錯誤
            // window.location.href = "/Home/Login";
        },
        
        complete: function(xhr, status) {
            console.log("=== AJAX Complete ===");
            console.log("Final Status:", status);
        }
    });
    
    console.log("AJAX 請求已發送");
}
```

### 步驟 2: 檢查 initializeApp 是否被執行

```javascript
async function initializeApp() {
    console.log("=== initializeApp 開始 ===");
    
    try {
        const profile = await liff.getProfile();
        console.log("? liff.getProfile() 成功");
        console.log("Profile:", profile);
        
        document.getElementById('displaynamefield').innerHTML = 
            profile.displayName + "<br/> 登入奉獻中，請稍待......";

        DisplayName = profile.displayName;
        UserId = profile.userId;
        GroupId = profile.aGroupId || "";
        RoomId = profile.aRoomId || "";
        ViewType = profile.aViewType || "";
        
        console.log("準備調用 UpdateLineUserId");
        
        // 取得 UserId 的Ajax
        UpdateLineUserId(UserId, GroupId, RoomId, ViewType);
    }
    catch (error) {
        console.error("? initializeApp 錯誤:", error);
        document.getElementById('displaynamefield').innerHTML = "錯誤:" + error;
    }
}
```

### 步驟 3: 檢查 LIFF 初始化流程

```javascript
window.onload = function (e) {
    console.log("=== window.onload 觸發 ===");
    console.log("LIFF ID:", '@TempData["Proponent"]');
    
    // 檢查 LIFF SDK 是否載入
    if (typeof liff === 'undefined') {
        console.error("? LIFF SDK 未載入！");
        document.getElementById('displaynamefield').innerHTML = "錯誤：LIFF SDK 未載入";
        return;
    } else {
        console.log("? LIFF SDK 已載入");
    }
    
    liff.init({ liffId: '@TempData["Proponent"]' })
        .then(() => {
            console.log("? LIFF 初始化成功");
            
            // ?查用?是否已登?
            if (!liff.isLoggedIn()) {
                console.log("?? 用戶未登入");
                document.getElementById('displaynamefield').innerHTML = "您還沒有登入LINE";
                ShowToast("您還沒有登入LINE", "error", 5000);
                liff.login();
                return;
            }
            else {
                console.log("? 用戶已登入");
                
                liff.permission.query("profile").then((permissionStatus) => {
                    console.log("權限狀態:", permissionStatus.state);
                    
                    if (permissionStatus.state === "granted") {
                        console.log("? 使用者已授權");
                        initializeApp();
                    }
                    else if (permissionStatus.state === "prompt") {
                        console.log("?? 需要授權");
                        document.getElementById('displaynamefield').innerHTML = "您並未授權，請先完成授權程序!";
                        liff.permission.requestAll();
                    }
                    else {
                        console.log("? LIFF Scope 未設定");
                        document.getElementById('displaynamefield').innerHTML = "Liff Scope沒有設定，所以無法操作!";
                    }
                });
            }
        })
        .catch(error => {
            console.error("? LIFF 初始化失敗:", error);
            document.getElementById('displaynamefield').innerHTML = "錯誤:" + error;
        });
};
```

---

## ?? 可能的問題點

### 問題 1: Url.Action 生成空字串或錯誤 URL

**檢查方法**:
```javascript
var ajaxUrl = '@Url.Action("SetupUserLineId", "Home")';
console.log("生成的 URL:", ajaxUrl);
console.log("URL 長度:", ajaxUrl.length);
```

**預期結果**: `/Home/SetupUserLineId`

**可能的錯誤**:
- 空字串 `""`
- 只有 `/Home/`
- 完整的絕對路徑（某些配置下）

**解決方案**:
```javascript
// 方案 1: 使用硬編碼 URL
url: '/Home/SetupUserLineId',

// 方案 2: 使用 @Url.Content
url: '@Url.Content("~/Home/SetupUserLineId")',

// 方案 3: 動態構建
url: window.location.origin + '/Home/SetupUserLineId',
```

### 問題 2: jQuery 未載入或載入順序錯誤

**症狀**: `$ is not defined` 或 `$.ajax is not a function`

**檢查**:
```javascript
if (typeof $ === 'undefined') {
    console.error("jQuery 未載入");
}
```

**原因**:
- jQuery 腳本載入失敗
- jQuery 載入在 AJAX 調用之後
- 腳本衝突

**解決方案**:
```html
<!-- 確認 jQuery 在最前面載入 -->
<script src="~/lib/jquery/dist/jquery.js"></script>
<!-- 確認路徑正確且檔案存在 -->
```

### 問題 3: LIFF 流程未完成

**症狀**: `initializeApp` 未被執行

**可能原因**:
- LIFF 初始化失敗
- 用戶未登入
- 權限檢查失敗
- 權限請求被拒絕

**檢查點**:
```
window.onload 觸發？
    ↓
LIFF SDK 載入？
    ↓
liff.init() 成功？
    ↓
liff.isLoggedIn() 返回 true？
    ↓
權限狀態為 "granted"？
    ↓
initializeApp() 被調用？
    ↓
liff.getProfile() 成功？
    ↓
UpdateLineUserId() 被調用？
```

### 問題 4: JavaScript 錯誤中斷執行

**症狀**: Console 中有紅色錯誤訊息

**常見錯誤**:
```
- Uncaught ReferenceError: $ is not defined
- Uncaught TypeError: Cannot read property 'dxLoadPanel' of undefined
- Uncaught TypeError: liff.getProfile is not a function
- Uncaught SyntaxError: Unexpected token
```

**檢查**:
```
開啟 Chrome DevTools (F12)
切換到 Console 標籤
查看是否有錯誤訊息
```

### 問題 5: CORS 或網路問題

**症狀**: AJAX 請求被瀏覽器阻擋

**檢查 Network 標籤**:
```
Status: (failed) → 網路錯誤
Status: (blocked:cors) → CORS 錯誤
Status: 0 → 請求未發送或被取消
```

---

## ??? 修復方案

### 方案 1: 添加完整的診斷日誌（推薦）

修改視圖檔案，添加上述的所有 Console 日誌：

```razor
<script>
    // 全局錯誤處理
    window.onerror = function(msg, url, lineNo, columnNo, error) {
        console.error("全局錯誤:", msg, "at", url, ":", lineNo, ":", columnNo);
        document.getElementById('displaynamefield').innerHTML = "JavaScript 錯誤: " + msg;
        return false;
    };

    // ... 其他代碼與上述診斷步驟相同
</script>
```

### 方案 2: 使用硬編碼 URL（臨時測試）

```javascript
function UpdateLineUserId(aUserLineId, aGroupId, aRoomId, aViewType) {
    $.ajax({
        url: '/Home/SetupUserLineId',  // 硬編碼，不使用 @Url.Action
        data: { 
            UserLineId: aUserLineId, 
            GroupId: aGroupId, 
            RoomId: aRoomId, 
            ViewType: aViewType
        },
        type: 'POST',
        success: function (data) {
            window.location.href = "/Home/QPayView/" + aUserLineId;
        },
        error: function (xhr, status, error) {
            console.error("AJAX Error:", status, error);
            getLoadPanelInstance().hide();
        }
    });
}
```

### 方案 3: 檢查並修復 jQuery 載入

```html
<!-- 在 <head> 中確認 jQuery 載入順序 -->
<script src="~/lib/jquery/dist/jquery.js"></script>
<script>
    // 驗證 jQuery 已載入
    if (typeof jQuery === 'undefined') {
        document.write('<script src="https://code.jquery.com/jquery-3.6.0.min.js"><\/script>');
    }
</script>
```

### 方案 4: 使用 Fetch API 替代 jQuery.ajax

```javascript
function UpdateLineUserId(aUserLineId, aGroupId, aRoomId, aViewType) {
    console.log("使用 Fetch API 發送請求");
    
    const formData = new FormData();
    formData.append('UserLineId', aUserLineId);
    formData.append('GroupId', aGroupId);
    formData.append('RoomId', aRoomId);
    formData.append('ViewType', aViewType);
    
    fetch('/Home/SetupUserLineId', {
        method: 'POST',
        body: formData
    })
    .then(response => {
        console.log("Response Status:", response.status);
        return response.json();
    })
    .then(data => {
        console.log("Response Data:", data);
        window.location.href = "/Home/QPayView/" + aUserLineId;
    })
    .catch(error => {
        console.error("Fetch Error:", error);
        document.getElementById('displaynamefield').innerHTML = "請求失敗: " + error;
    });
}
```

---

## ?? 測試清單

執行以下測試來診斷問題：

### 測試 1: Console 日誌檢查

```
□ 開啟 Chrome DevTools (F12)
□ 切換到 Console 標籤
□ 在 LINE 中開啟 LIFF URL
□ 觀察日誌輸出

預期看到:
? "=== window.onload 觸發 ==="
? "? LIFF SDK 已載入"
? "? LIFF 初始化成功"
? "? 用戶已登入"
? "? 使用者已授權"
? "=== initializeApp 開始 ==="
? "? liff.getProfile() 成功"
? "準備調用 UpdateLineUserId"
? "=== UpdateLineUserId 被調用 ==="
? "AJAX URL: /Home/SetupUserLineId"
? "? jQuery 已載入"
? "準備發送 AJAX 請求..."
? "=== beforeSend 觸發 ==="
? "=== AJAX Success ===" 或 "=== AJAX Error ==="
```

### 測試 2: Network 標籤檢查

```
□ 切換到 Network 標籤
□ 勾選 "Preserve log"
□ 重新載入頁面
□ 查找 SetupUserLineId 請求

預期看到:
? 有 SetupUserLineId 請求
? Status Code: 200 OK
? Response: {"status":"1"}
```

### 測試 3: 手動測試 URL

```
在 Console 中執行:
> '@Url.Action("SetupUserLineId", "Home")'

預期結果: "/Home/SetupUserLineId"
```

### 測試 4: 手動測試 AJAX

```
在 Console 中執行:
> $.ajax({
    url: '/Home/SetupUserLineId',
    data: { UserLineId: 'test', GroupId: '', RoomId: '', ViewType: '' },
    type: 'POST',
    success: function(data) { console.log('Success:', data); },
    error: function(xhr, status, error) { console.log('Error:', status, error); }
  });

預期結果: Success: {status: "1"}
```

---

## ?? 快速診斷決策樹

```
UpdateLineUserId 沒有被呼叫到？
    ├─ Console 中看到 "=== UpdateLineUserId 被調用 ==="？
    │   ├─ 是 → 問題在 AJAX 請求
    │   │   ├─ 看到 "=== beforeSend 觸發 ==="？
    │   │   │   ├─ 是 → AJAX 已發送，問題在後端或網路
    │   │   │   │   └─ 檢查 Network 標籤，查看 Status Code
    │   │   │   └─ 否 → jQuery 問題
    │   │   │       └─ 檢查 jQuery 是否載入
    │   │   └─ 看到 "AJAX URL: ..."？
    │   │       ├─ URL 正確 → 繼續
    │   │       └─ URL 錯誤/空白 → Url.Action 問題
    │   └─ 否 → 問題在 UpdateLineUserId 之前
    │       ├─ 看到 "準備調用 UpdateLineUserId"？
    │       │   ├─ 是 → initializeApp 執行但 UpdateLineUserId 未執行
    │       │   │   └─ 可能有 JavaScript 錯誤
    │       │   └─ 否 → initializeApp 未執行
    │       │       └─ 檢查 LIFF 流程
    │       └─ 看到 "=== initializeApp 開始 ==="？
    │           ├─ 是 → liff.getProfile() 可能失敗
    │           │   └─ 檢查 Console 錯誤
    │           └─ 否 → LIFF 初始化或權限問題
    │               └─ 檢查 LIFF 相關日誌
    └─ Console 中有紅色錯誤？
        ├─ 是 → JavaScript 錯誤中斷執行
        │   └─ 修復錯誤後重試
        └─ 否 → LIFF 流程未完成
            └─ 檢查 LIFF 初始化和權限
```

---

## ?? 最可能的原因（排序）

### 1. ? LIFF 流程未完成 (最常見)
- 用戶未登入或未授權
- LIFF 初始化失敗
- `initializeApp` 未執行

**檢查**: Console 中是否看到 "=== initializeApp 開始 ==="

### 2. ? JavaScript 錯誤
- LoadPanel 相關錯誤（已修復）
- jQuery 未載入
- 其他語法錯誤

**檢查**: Console 中是否有紅色錯誤訊息

### 3. Url.Action 生成錯誤 URL
- 返回空字串
- 返回錯誤路徑

**檢查**: Console 中 "AJAX URL:" 的值

### 4. 網路或 CORS 問題
- 請求被瀏覽器阻擋
- 伺服器無回應

**檢查**: Network 標籤中的請求狀態

---

## ?? 立即執行的診斷步驟

### 步驟 1: 添加診斷日誌

1. 修改 `DediationLineLoginView.cshtml`
2. 添加上述的 Console.log 代碼
3. 儲存並部署

### 步驟 2: 在 LINE 中測試

1. 開啟 Chrome 並連接手機進行遠端除錯
   - 或在電腦瀏覽器中模擬 LIFF
2. 開啟 DevTools
3. 開啟 LIFF URL
4. 觀察 Console 輸出

### 步驟 3: 分析結果

根據 Console 輸出判斷問題點：

| 看到的最後一條日誌 | 問題位置 | 解決方案 |
|-------------------|---------|---------|
| "=== window.onload 觸發 ===" 之後沒有其他日誌 | LIFF 初始化失敗 | 檢查 LIFF ID 和配置 |
| "?? 用戶未登入" | 用戶未登入 LINE | 確保在 LINE 應用程式中開啟 |
| "?? 需要授權" | 權限未授予 | 完成授權流程 |
| "準備調用 UpdateLineUserId" 之後沒有其他日誌 | JavaScript 錯誤 | 檢查 Console 錯誤訊息 |
| "=== UpdateLineUserId 被調用 ===" | AJAX 問題 | 繼續檢查 AJAX URL 和 jQuery |
| "=== beforeSend 觸發 ===" | 後端或網路問題 | 檢查 Network 標籤 |
| "=== AJAX Success ===" | **成功！** | 問題已解決 |

---

## ?? 需要提供的資訊

如果診斷後仍然無法解決，請提供：

1. **Console 完整日誌** (從 window.onload 到最後)
2. **Network 標籤截圖** (顯示所有請求)
3. **錯誤訊息** (如果有紅色錯誤)
4. **測試環境**:
   - 瀏覽器: Chrome / Safari / LINE 內建
   - 設備: 電腦 / iPhone / Android
   - 測試方式: LINE 應用程式 / 電腦模擬

---

**建議**: 先添加診斷日誌，然後在 LINE 中測試，根據 Console 輸出來判斷具體問題。這樣可以快速定位問題所在。
