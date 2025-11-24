# DediationLineLoginView 持續失敗 - 綜合診斷報告

## ?? 問題分析

根據最新檢查，發現以下問題：

### 問題 1: 視圖文件缺少 LoadPanel 定義 ??

```html
<!-- DediationLineLoginView.cshtml 第 193 行 -->
<div id="LoadPanelSection"></div>
```

JavaScript 嘗試使用 LoadPanel，但視圖中沒有定義：
```javascript
// 第 248 行
function getLoadPanelInstance() {
    return $("#loadPanel").dxLoadPanel("instance");  // ? #loadPanel 不存在
}
```

### 問題 2: 視圖位置可能不正確

視圖文件位置：`Views\Home\DediationLineLoginView.cshtml`
Controller 期望位置：`Views\Dedication\DediationLineLoginView.cshtml` 或 `Views\Home\`

由於 ASP.NET Core MVC 的視圖查找順序：
1. `Views/Dedication/DediationLineLoginView.cshtml` (優先)
2. `Views/Shared/DediationLineLoginView.cshtml`
3. `Views/Home/DediationLineLoginView.cshtml` (向後相容)

---

## ??? 修復方案

### 方案 1: 修復視圖文件（推薦）

需要在視圖中添加 LoadPanel 定義：

```razor
<!-- 在 <div id="ToastContainer"> 之前添加 -->
<div id="LoadPanelContainer">
    @(Html.DevExtreme().LoadPanel()
        .ID("loadPanel")
        .ShadingColor("rgba(0,0,0,0.4)")
        .Position(p => p.Of("#LoadPanelSection"))
        .Visible(false)
        .ShowIndicator(true)
        .ShowPane(true)
        .Shading(true)
        .CloseOnOutsideClick(false)
        .Message("登入中，請稍候...")
    )
</div>
```

### 方案 2: 移除不使用的 LoadPanel 代碼

如果不需要 LoadPanel，簡化 JavaScript：

```javascript
// 修改 error 處理
error: function (obj) {
    // 移除 getLoadPanelInstance().hide();
    document.getElementById('displaynamefield').innerHTML = "登入失敗，請稍後再試";
    ShowToast("登入失敗", "error", 3000);
    // 不自動重導向到登入頁面
}
```

---

## ?? 完整修復的 DediationLineLoginView.cshtml

讓我創建一個完整修復的版本...

---

## ?? 測試步驟

### 步驟 1: 檢查視圖位置

```powershell
# 檢查視圖文件是否存在
Test-Path "ChurchReport\Views\Home\DediationLineLoginView.cshtml"
Test-Path "ChurchReport\Views\Dedication\DediationLineLoginView.cshtml"
```

### 步驟 2: 檢查 LIFF ID

```
確認 LIFF ID 是否有效:
2007156647-OYnN8BKy
```

登入 LINE Developers Console 驗證：
1. https://developers.line.biz/console/
2. 選擇你的 Provider
3. 選擇 LIFF App
4. 確認 LIFF ID 正確

### 步驟 3: 檢查端點 URL

```
Endpoint URL (LIFF 設定中):
https://sunnyvalechback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
```

### 步驟 4: 瀏覽器開發者工具檢查

1. 開啟 Chrome DevTools (F12)
2. 切換到 Console 標籤
3. 查看是否有 JavaScript 錯誤
4. 切換到 Network 標籤
5. 查看 SetupUserLineId 請求狀態

---

## ?? 常見錯誤訊息及解決方案

### 錯誤 1: "Cannot read property 'dxLoadPanel' of undefined"

**原因**: LoadPanel 未定義

**解決**: 添加 LoadPanel 定義或移除相關代碼

### 錯誤 2: "404 Not Found"

**原因**: 
- IIS 未運行
- 應用程式池停止
- 路由配置錯誤

**解決**: 
```powershell
iisreset /restart
```

### 錯誤 3: "LIFF init failed"

**原因**:
- LIFF ID 錯誤
- 網域未在 LIFF 白名單中
- HTTPS 設定問題

**解決**:
1. 檢查 LIFF ID
2. 在 LINE Developers Console 中添加網域到白名單
3. 確認 SSL 憑證有效

### 錯誤 4: "您還沒有登入LINE"

**原因**:
- 用戶未登入 LINE
- LIFF SDK 初始化失敗

**解決**:
- 確認在 LINE 應用程式中開啟
- 檢查 LIFF SDK 版本

---

## ?? 快速修復腳本

### 檢查並修復視圖文件

```powershell
# 檢查視圖文件
$viewPath = "ChurchReport\Views\Home\DediationLineLoginView.cshtml"
if (Test-Path $viewPath) {
    Write-Host "? 視圖文件存在: $viewPath"
    
    # 檢查是否包含 LoadPanel
    $content = Get-Content $viewPath -Raw
    if ($content -like "*dxLoadPanel*") {
        Write-Host "?? 視圖中有 LoadPanel 引用但可能定義不完整"
    } else {
        Write-Host "? 視圖中沒有 LoadPanel 引用"
    }
} else {
    Write-Host "? 視圖文件不存在: $viewPath"
}

# 檢查路由配置
$startupPath = "ChurchReport\Startup.cs"
$startupContent = Get-Content $startupPath -Raw
if ($startupContent -like "*DediationLineLoginView*") {
    Write-Host "? Startup.cs 中有路由配置"
} else {
    Write-Host "? Startup.cs 中缺少路由配置"
}

# 檢查 Controller
$controllerPath = "ChurchReport\Controllers\DedicationController.cs"
$controllerContent = Get-Content $controllerPath -Raw
if ($controllerContent -like "*public IActionResult DediationLineLoginView*") {
    Write-Host "? DedicationController 中有方法定義"
} else {
    Write-Host "? DedicationController 中缺少方法定義"
}

# 檢查 HomeController 向後相容
$homeControllerPath = "ChurchReport\Controllers\HomeController.cs"
$homeContent = Get-Content $homeControllerPath -Raw
if ($homeContent -like "*SetupUserLineIdRedirect*") {
    Write-Host "? HomeController 中有 SetupUserLineId 向後相容路由"
} else {
    Write-Host "? HomeController 中缺少 SetupUserLineId 向後相容路由"
}
```

---

## ?? 診斷決策樹

```
開始診斷
    ↓
能訪問 URL？
    ├─ 否 → 404 錯誤
    │     ├─ 檢查 IIS 運行狀態
    │     ├─ 檢查應用程式池
    │     └─ 檢查 Port 479 綁定
    └─ 是 ↓
        ↓
頁面正常顯示？
    ├─ 否 → 檢視錯誤訊息
    │     ├─ 500 錯誤 → 檢查應用程式日誌
    │     └─ 其他 → 檢查視圖文件
    └─ 是 ↓
        ↓
LIFF 初始化成功？
    ├─ 否 → Console 中有錯誤
    │     ├─ LIFF ID 錯誤
    │     ├─ 網域未授權
    │     └─ HTTPS 問題
    └─ 是 ↓
        ↓
取得使用者資料成功？
    ├─ 否 → 權限問題
    │     ├─ 用戶未授權 profile
    │     └─ LIFF scope 設定錯誤
    └─ 是 ↓
        ↓
AJAX 請求成功？
    ├─ 否 → Network 錯誤
    │     ├─ SetupUserLineId 404 → 檢查路由
    │     ├─ SetupUserLineId 500 → 檢查 Controller
    │     └─ SetupUserLineId timeout → 檢查 CRM 連線
    └─ 是 ↓
        ↓
重導向成功？
    ├─ 否 → QPayView 問題
    │     └─ 檢查 QPayView 路由
    └─ 是 → ? 登入成功！
```

---

## ?? 重點檢查項目（按優先順序）

### 優先級 1: 伺服器運行狀態
```powershell
# 1. IIS 服務
sc query W3SVC

# 2. 應用程式池
Import-Module WebAdministration
Get-WebAppPoolState "ChurchReport"

# 3. Port 監聽
netstat -ano | findstr :479
```

### 優先級 2: 視圖文件完整性
```
□ 視圖文件存在
□ LoadPanel 正確定義（如果使用）
□ LIFF SDK 正確載入
□ JavaScript 無語法錯誤
```

### 優先級 3: LIFF 配置
```
□ LIFF ID 正確
□ Endpoint URL 正確
□ 網域在白名單中
□ Scope 包含 profile
```

### 優先級 4: 後端 API
```
□ SetupUserLineId endpoint 存在
□ Controller 方法正確
□ CRM 連線正常
□ 返回正確的 JSON
```

---

## ?? 何時聯絡技術支援

立即聯絡如果：
- ? 執行所有診斷步驟後仍然失敗
- ? 瀏覽器 Console 顯示嚴重錯誤
- ? 應用程式日誌中有異常
- ? 多個用戶報告相同問題

準備提供：
1. 瀏覽器 Console 完整錯誤訊息
2. Network 標籤中的請求/回應
3. 應用程式日誌 (stdout, Trace.log)
4. IIS 日誌
5. 使用的 URL 和 LIFF ID

---

## ?? 臨時解決方案

如果需要立即恢復服務：

### 選項 1: 使用簡化版視圖

創建一個最簡化的版本，移除所有可能導致錯誤的元素。

### 選項 2: 直接導向 QPayView

修改 LIFF 設定，直接開啟：
```
https://sunnyvalechback.speechmessage.com.tw:479/Dedication/QPayView/{LineUserId}
```

繞過 DediationLineLoginView。

### 選項 3: 使用 LineIdLoginView 代替

如果小組管理的 LINE 登入正常，可以暫時使用相同機制。

---

## ?? 修復檢查清單

完成這些步驟以確保修復成功：

- [ ] 視圖文件包含完整的 LoadPanel 定義
- [ ] JavaScript 無語法錯誤
- [ ] LIFF ID 在 LINE Developers Console 中有效
- [ ] Endpoint URL 正確配置
- [ ] 網域在 LIFF 白名單中
- [ ] IIS 和應用程式池運行正常
- [ ] SetupUserLineId endpoint 正常回應
- [ ] 瀏覽器測試通過
- [ ] LINE 應用程式中測試通過
- [ ] 多個測試帳號驗證成功

---

**下一步行動**:
1. 修復視圖文件中的 LoadPanel 問題
2. 確認 LIFF 配置正確
3. 執行完整測試
4. 監控錯誤日誌

**技術支援**: tech@sunnyvalech.org
