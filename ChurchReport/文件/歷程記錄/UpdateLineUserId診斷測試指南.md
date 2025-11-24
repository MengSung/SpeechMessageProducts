# UpdateLineUserId 診斷測試指南

## ? 已完成的修復

我已經在 `DediationLineLoginView.cshtml` 中添加了**完整的診斷日誌**，現在可以追蹤整個登入流程。

---

## ?? 立即執行的測試步驟

### 步驟 1: 部署更新的視圖文件

確保更新的 `DediationLineLoginView.cshtml` 已部署到伺服器。

### 步驟 2: 重啟 IIS

```powershell
# 以管理員身份執行
iisreset /restart
```

### 步驟 3: 開啟 Chrome DevTools

```
1. 開啟 Chrome 瀏覽器
2. 按 F12 開啟開發者工具
3. 切換到 Console 標籤
4. 切換到 Network 標籤（同時開啟）
```

### 步驟 4: 在 LINE 中測試

```
在 LINE 應用程式中開啟 LIFF URL:
https://sunnyvalechback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
```

### 步驟 5: 觀察 Console 輸出

**預期看到的日誌順序**:

```
? === window.onload 觸發 ===
? LIFF ID: 2007156647-OYnN8BKy
? ? LIFF SDK 已載入
? ? LIFF 初始化成功
? ? 用戶已登入 LINE
? 權限狀態: granted
? ? 使用者已授權 profile 權限
? === initializeApp 開始 ===
? ? liff.getProfile() 成功
? Profile: {userId: "U...", displayName: "...", ...}
? DisplayName: ...
? UserId: U...
? GroupId: 
? RoomId: 
? ViewType: 
? 準備調用 UpdateLineUserId
? === UpdateLineUserId 被調用 ===
? UserLineId: U...
? GroupId: 
? RoomId: 
? ViewType: 
? ? jQuery 已載入，版本: 3.x.x
? AJAX URL: /Home/SetupUserLineId
? AJAX URL Type: string
? AJAX URL Length: 23
? 準備發送 AJAX 請求...
? AJAX 請求已發送
? === AJAX beforeSend 觸發 ===
? URL: /Home/SetupUserLineId
? Type: POST
? Data: UserLineId=U...&GroupId=&RoomId=&ViewType=
? === AJAX Success ===
? Response: {status: "1"}
? Response Type: object
? Status: 1
? 準備重導向到: /Home/QPayView/U...
? === AJAX Complete ===
? Final Status: success
```

---

## ?? 根據日誌判斷問題

### 情況 1: 看到 "? LIFF SDK 未載入"

**問題**: LIFF SDK 腳本載入失敗

**解決方案**:
```html
<!-- 確認視圖底部有這行 -->
<script src="https://static.line-scdn.net/liff/edge/2/sdk.js"></script>

<!-- 檢查網路連線 -->
<!-- 確認 URL 沒有被防火牆阻擋 -->
```

### 情況 2: 看到 "? LIFF 初始化失敗"

**問題**: LIFF ID 錯誤或 LIFF 配置問題

**解決方案**:
1. 檢查 LIFF ID 是否正確: `2007156647-OYnN8BKy`
2. 登入 LINE Developers Console 驗證 LIFF 應用程式狀態
3. 確認 Endpoint URL 配置正確

### 情況 3: 看到 "?? 用戶未登入 LINE"

**問題**: 用戶未在 LINE 中開啟或 LINE 登入狀態異常

**解決方案**:
- 確保在 LINE 應用程式中開啟 URL
- 重新登入 LINE
- 清除 LINE 快取後重試

### 情況 4: 看到 "?? 需要請求授權"

**問題**: 用戶尚未授權 profile 權限

**解決方案**:
- 系統會自動請求授權
- 用戶需要點擊「允許」
- 如果授權被拒絕，需要重新開啟 LIFF

### 情況 5: 看到 "=== initializeApp 開始 ===" 後沒有其他日誌

**問題**: `liff.getProfile()` 失敗

**解決方案**:
- 檢查 Console 是否有錯誤訊息
- 確認 LIFF Scope 包含 `profile`
- 在 LINE Developers Console 檢查 LIFF 設定

### 情況 6: 看到 "? jQuery 未載入"

**問題**: jQuery 腳本載入失敗或載入順序錯誤

**解決方案**:
```html
<!-- 確認視圖中有這行，且在其他腳本之前 -->
<script src="~/lib/jquery/dist/jquery.js"></script>

<!-- 檢查路徑是否正確 -->
<!-- 檢查檔案是否存在 -->
```

### 情況 7: 看到 "? AJAX URL 為空"

**問題**: `@Url.Action` 生成失敗

**解決方案**:
- 系統會自動使用備用 URL: `/Home/SetupUserLineId`
- 檢查視圖是否正確編譯
- 確認 Razor 引擎正常運作

### 情況 8: 看到 "=== AJAX Error ==="

**問題**: AJAX 請求失敗

**根據 Status Code 判斷**:

#### Status Code: 404
```
原因: 路由未找到
解決: 
1. 確認 HomeController.SetupUserLineIdRedirect 存在
2. 重啟 IIS: iisreset /restart
3. 檢查應用程式池狀態
```

#### Status Code: 500
```
原因: 後端代碼執行錯誤
解決:
1. 查看 Logs\Trace.log
2. 查看 Logs\stdout*.log
3. 檢查依賴注入是否成功
4. 檢查 CRM 連線
```

#### Status Code: 0
```
原因: 請求未發送或被取消
解決:
1. 檢查網路連線
2. 檢查 CORS 設定
3. 檢查防火牆規則
```

### 情況 9: 完全沒有日誌輸出

**問題**: `window.onload` 未觸發或 Console 未開啟

**解決方案**:
1. 確認 Chrome DevTools 已開啟
2. 確認在 Console 標籤
3. 重新載入頁面
4. 檢查是否有 JavaScript 語法錯誤阻止執行

---

## ?? Network 標籤檢查

同時觀察 Network 標籤：

### 預期請求順序

```
1. DediationLineLoginView/2007156647-OYnN8BKy  → 200 OK (HTML)
2. jquery.js                                   → 200 OK (Script)
3. bootstrap.js                                → 200 OK (Script)
4. dx.all.js                                   → 200 OK (Script)
5. liff SDK                                    → 200 OK (Script)
6. sunnyvalech.jpg                            → 200 OK (Image)
7. SetupUserLineId                            → 200 OK (XHR/AJAX)
   - Request Method: POST
   - Request Payload: UserLineId=U...&GroupId=&RoomId=&ViewType=
   - Response: {"status":"1"}
8. QPayView/U...                              → 200 OK (Redirect)
```

### 如果 SetupUserLineId 不在清單中

**原因**: AJAX 根本沒有發送

**檢查**:
- Console 中是否有 "準備發送 AJAX 請求..." 日誌
- 是否有 JavaScript 錯誤中斷執行
- jQuery 是否正確載入

### 如果 SetupUserLineId 顯示 (failed)

**原因**: 網路問題或 CORS 錯誤

**檢查**:
- Console 中的錯誤訊息
- 是否有紅色 CORS 錯誤
- 伺服器是否可訪問

---

## ?? 快速診斷決策圖

```
開啟 Chrome DevTools Console
    ↓
載入 LIFF URL
    ↓
看到 "=== window.onload 觸發 ==="？
    ├─ 否 → 頁面載入問題
    │       └─ 檢查網路、IIS、應用程式池
    └─ 是 ↓
看到 "? LIFF SDK 已載入"？
    ├─ 否 → LIFF SDK 載入失敗
    │       └─ 檢查腳本 URL、網路連線
    └─ 是 ↓
看到 "? LIFF 初始化成功"？
    ├─ 否 → LIFF 初始化失敗
    │       └─ 檢查 LIFF ID、配置
    └─ 是 ↓
看到 "? 用戶已登入 LINE"？
    ├─ 否 → 用戶未登入
    │       └─ 在 LINE 中開啟、重新登入
    └─ 是 ↓
看到 "? 使用者已授權 profile 權限"？
    ├─ 否 → 權限問題
    │       └─ 完成授權流程
    └─ 是 ↓
看到 "=== initializeApp 開始 ==="？
    ├─ 否 → 權限檢查後出錯
    │       └─ 檢查 Console 錯誤
    └─ 是 ↓
看到 "? liff.getProfile() 成功"？
    ├─ 否 → getProfile 失敗
    │       └─ 檢查 LIFF Scope 設定
    └─ 是 ↓
看到 "=== UpdateLineUserId 被調用 ==="？
    ├─ 否 → initializeApp 未完成
    │       └─ 檢查 Console 錯誤
    └─ 是 ↓
看到 "? jQuery 已載入"？
    ├─ 否 → jQuery 問題
    │       └─ 檢查 jQuery 載入
    └─ 是 ↓
看到 "準備發送 AJAX 請求..."？
    ├─ 否 → AJAX URL 檢查失敗
    │       └─ 查看 AJAX URL 日誌
    └─ 是 ↓
看到 "=== AJAX beforeSend 觸發 ==="？
    ├─ 否 → AJAX 未發送
    │       └─ 檢查 jQuery.ajax 錯誤
    └─ 是 ↓
看到 "=== AJAX Success ==="？
    ├─ 否 → 看到 "=== AJAX Error ==="
    │       └─ 後端或網路問題
    │           └─ 檢查 Status Code
    └─ 是 → ? 成功！
```

---

## ?? 測試報告範本

測試完成後，請填寫以下資訊：

```
【測試環境】
- 瀏覽器: Chrome / Safari / LINE 內建
- 設備: 電腦 / iPhone / Android
- 測試時間: YYYY-MM-DD HH:mm

【Console 日誌】
（複製 Console 中的所有輸出）

【Network 狀態】
- 是否看到 SetupUserLineId 請求: 是 / 否
- Status Code: ___
- Response: ___

【錯誤訊息】
（如果有紅色錯誤，請完整複製）

【問題描述】
（描述看到的問題）

【最後看到的成功日誌】
（例如: "=== UpdateLineUserId 被調用 ==="）
```

---

## ?? 最可能的問題與解決方案

根據經驗，最常見的問題是：

### 1. LIFF 流程未完成 (60%)

**症狀**: 沒有看到 "=== UpdateLineUserId 被調用 ==="

**解決方案**:
- 確保在 LINE 應用程式中開啟
- 完成授權流程
- 檢查 LIFF ID 和配置

### 2. 後端 API 404 錯誤 (20%)

**症狀**: 看到 "=== AJAX Error ===" 和 Status Code: 404

**解決方案**:
```powershell
# 重啟 IIS
iisreset /restart

# 檢查應用程式池
Get-WebAppPoolState "ChurchReport"

# 如果停止，啟動它
Start-WebAppPool "ChurchReport"
```

### 3. jQuery 未載入 (10%)

**症狀**: 看到 "? jQuery 未載入"

**解決方案**:
- 檢查 jQuery 腳本路徑
- 確認 jQuery 檔案存在
- 檢查網路連線

### 4. JavaScript 錯誤 (10%)

**症狀**: Console 中有紅色錯誤訊息

**解決方案**:
- 修復錯誤後重新測試
- 確認所有腳本正確載入
- 檢查語法錯誤

---

## ?? 下一步行動

1. **部署更新的視圖文件**
2. **重啟 IIS**
3. **在 LINE 中開啟 LIFF URL**
4. **開啟 Chrome DevTools**
5. **觀察 Console 輸出**
6. **根據日誌判斷問題**
7. **提供測試結果**

**如果成功看到 "=== AJAX Success ==="，問題就解決了！** ?

**如果失敗，請提供完整的 Console 日誌和 Network 截圖，我會根據實際情況提供解決方案。** ??
