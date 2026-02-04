# DediationLineLoginView 頁面未顯示 - 快速參考

## ?? 問題：DediationLineLoginView 連頁面都沒有顯示

---

## ? 30 秒快速修復

### 步驟 1: 執行緊急修復（以管理員身份）
```batch
緊急修復DediationLineLoginView.bat
```

### 步驟 2: 在瀏覽器測試
```
https://localhost:479/Dedication/DediationLineLoginView/test
```

### 步驟 3: 觀察結果
- ? 頁面顯示 → 問題已解決
- ? 404 錯誤 → 路由問題
- ? 500 錯誤 → 後端錯誤
- ? 空白頁面 → 視圖或 JavaScript 問題
- ? 無法連線 → IIS 問題

---

## ?? 根據瀏覽器顯示判斷

### 顯示：404 Not Found

**原因**: 路由未找到

**快速修復**:
```powershell
# 重啟 IIS
iisreset /restart

# 檢查 Controller 方法是否存在
Get-Content "Controllers\DedicationController.cs" | Select-String "DediationLineLoginView"
```

**檢查清單**:
- [ ] DedicationController.DediationLineLoginView 方法存在
- [ ] 有 [Route] 屬性
- [ ] IIS 已重啟
- [ ] 編譯檔案是最新的

### 顯示：500 Internal Server Error

**原因**: Controller 執行錯誤

**快速修復**:
```powershell
# 查看日誌
Get-Content "Logs\Trace.log" -Tail 50
```

**常見錯誤**:
- `TempData["Proponent"]` 為 null
- `InMemoryContext` 初始化失敗
- 視圖檔案不存在
- Model 為 null

### 顯示：空白頁面

**原因**: 視圖渲染問題或 JavaScript 錯誤

**快速修復**:
```
1. F12 → Console 查看錯誤
2. F12 → Network 查看 Response
```

**檢查清單**:
- [ ] Console 沒有 JavaScript 錯誤
- [ ] Response 有 HTML 內容
- [ ] CSS/JS 檔案載入成功

### 顯示：無法連線

**原因**: IIS 未運行或 Port 問題

**快速修復**:
```powershell
# 啟動 IIS
net start W3SVC

# 啟動應用程式池
Import-Module WebAdministration
Start-WebAppPool "ChurchReport"

# 檢查 Port
netstat -ano | findstr ":479"
```

---

## ??? 完整診斷流程

```
執行: 緊急修復DediationLineLoginView.bat (2 分鐘)
    ↓
    成功？
    ├─ 是 → ? 測試 URL
    └─ 否 ↓
執行: 診斷DediationLineLoginView頁面未顯示.ps1 (5 分鐘)
    ↓
查看診斷報告: DediationLineLoginView診斷結果.txt
    ↓
根據報告中標記為 ? 的項目進行修復
    ↓
重新測試
```

---

## ?? 測試 URL

### 本機測試（在伺服器上）
```
https://localhost:479/Dedication/DediationLineLoginView/test
```

### 實際 LIFF URL（在 LINE 中）
```
https://jesusback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
```

### 簡化測試（不需參數）
```
https://localhost:479/Dedication/DediationLineLoginView
```

---

## ?? 瀏覽器診斷步驟

```
1. F12 開啟開發者工具
2. Network 標籤 + Console 標籤
3. 訪問測試 URL
4. 觀察:
   - Network: Status Code、Response
   - Console: 錯誤訊息
```

---

## ?? 最可能的原因（排序）

### 1. IIS 應用程式池停止 (40%)
```powershell
# 檢查
Get-WebAppPoolState "ChurchReport"

# 修復
Start-WebAppPool "ChurchReport"
iisreset /restart
```

### 2. 路由配置問題 (30%)
```powershell
# 檢查 Controller 方法
Get-Content "Controllers\DedicationController.cs" | Select-String "DediationLineLoginView" -Context 2,5

# 修復
# 確認方法存在且有 [Route] 屬性
# 重啟 IIS
```

### 3. 視圖檔案問題 (20%)
```powershell
# 檢查
Test-Path "Views\Home\DediationLineLoginView.cshtml"
Test-Path "Views\Dedication\DediationLineLoginView.cshtml"

# 修復
# 確認視圖檔案存在且不為空
```

### 4. IIS 服務未運行 (10%)
```powershell
# 檢查
sc query W3SVC

# 修復
net start W3SVC
```

---

## ?? 相關檔案位置

```
Controllers:
  Controllers\DedicationController.cs

Views:
  Views\Home\DediationLineLoginView.cshtml
  Views\Dedication\DediationLineLoginView.cshtml

Logs:
  Logs\Trace.log
  Logs\stdout*.log

Startup:
  Startup.cs
```

---

## ?? 決策樹（30 秒判斷）

```
頁面沒有顯示
    ↓
瀏覽器顯示什麼？
    ├─ 404 Not Found → 路由問題 → 重啟 IIS
    ├─ 500 Error → 後端錯誤 → 查看日誌
    ├─ 空白頁面 → 視圖問題 → 檢查 Console
    ├─ 無法連線 → IIS 問題 → 檢查服務
    └─ 無限載入 → 重導向迴圈 → 檢查 Controller
```

---

## ?? 需要協助時提供

如果執行所有步驟後仍然失敗，請提供：

```
1. 診斷報告: DediationLineLoginView診斷結果.txt

2. 瀏覽器截圖:
   - 顯示的內容
   - F12 → Network 標籤
   - F12 → Console 標籤

3. 日誌:
   - Logs\Trace.log (最後 50 行)
   - Logs\stdout*.log (如果存在)

4. 測試結果:
   - 本機測試: 成功/失敗
   - 實際 URL 測試: 成功/失敗
   - Status Code: ___
   - Console 錯誤: ___
```

---

## ? 成功標準

當您看到以下內容時，問題就解決了：

```
? 頁面正常顯示
? 可以看到「神住611靈糧堂」標題
? 可以看到相片輪播
? 可以看到聯絡資訊
? Console 沒有錯誤
? LIFF 初始化成功（如果在 LINE 中測試）
```

---

## ?? 立即執行

```batch
# 1. 緊急修復（以管理員身份執行）
緊急修復DediationLineLoginView.bat

# 2. 瀏覽器測試
開啟: https://localhost:479/Dedication/DediationLineLoginView/test

# 3. 如果失敗，執行完整診斷
診斷DediationLineLoginView頁面未顯示.ps1
```

---

**現在請立即執行緊急修復並回報結果！** ??

**如果成功看到頁面，恭喜您，問題已解決！** ??
