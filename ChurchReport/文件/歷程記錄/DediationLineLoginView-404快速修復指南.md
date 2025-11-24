# DediationLineLoginView 404 錯誤快速修復指南

## ?? 緊急情況 - 立即執行

### 1分鐘快速修復
```powershell
# 以管理員身份執行
iisreset /restart
```

等待 30 秒後測試 URL。

---

## ?? 診斷檢查清單（按優先順序）

### ? 檢查 1: IIS 服務狀態
```powershell
sc query W3SVC
# 預期: STATE = RUNNING
```

**如果未運行**:
```powershell
net start W3SVC
```

---

### ? 檢查 2: 應用程式池狀態
```powershell
# PowerShell
Import-Module WebAdministration
Get-WebAppPoolState -Name "ChurchReport"
# 預期: Started
```

**如果已停止**:
```powershell
Start-WebAppPool -Name "ChurchReport"
```

---

### ? 檢查 3: Port 479 監聽
```powershell
netstat -ano | findstr :479
# 預期: 看到 LISTENING
```

**如果沒有監聽**:
- 檢查 IIS 網站綁定
- 檢查防火牆規則

---

### ? 檢查 4: 測試本機連線
```powershell
# PowerShell
Invoke-WebRequest -Uri "https://localhost:479/" -UseBasicParsing
# 預期: 返回 HTTP 狀態碼 200 或 302
```

**如果失敗**:
- 查看應用程式日誌
- 檢查 web.config

---

## ??? 常見問題快速修復

### 問題 1: 應用程式池頻繁停止

**原因**: 記憶體不足或應用程式錯誤

**修復**:
```powershell
# 1. 增加應用程式池記憶體限制
Set-ItemProperty "IIS:\AppPools\ChurchReport" -name recycling.periodicRestart.memory -value 0

# 2. 禁用快速失敗保護（臨時）
Set-ItemProperty "IIS:\AppPools\ChurchReport" -name failure.rapidFailProtection -value $false

# 3. 查看錯誤日誌
Get-Content "Logs\stdout*.log" -Tail 50
```

---

### 問題 2: SSL 憑證問題

**症狀**: HTTPS 無法連線，HTTP 可以

**檢查**:
```powershell
# 檢查 SSL 綁定
netsh http show sslcert ipport=0.0.0.0:479
```

**修復**: (需要憑證指紋和應用程式 GUID)
```powershell
# 獲取憑證指紋
Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -like "*speechmessage.com.tw*" }

# 綁定憑證 (替換 [指紋] 和 [GUID])
netsh http add sslcert ipport=0.0.0.0:479 certhash=[指紋] appid={[GUID]}
```

---

### 問題 3: 路由不工作

**症狀**: 其他頁面可以訪問，但 DediationLineLoginView 不行

**檢查**:
```powershell
# 測試其他路徑
Invoke-WebRequest -Uri "https://localhost:479/Home/Login" -UseBasicParsing
```

**修復**:
1. 確認 `Startup.cs` 中有路由配置
2. 確認 `DedicationController.cs` 存在且已編譯
3. 清除 ASP.NET 暫存並重新部署

---

### 問題 4: 權限問題

**症狀**: 503 錯誤或無法啟動

**修復**:
```powershell
# 設定目錄權限
$path = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport"
icacls $path /grant "IIS_IUSRS:(OI)(CI)F" /T
```

---

## ?? 診斷流程圖

```
開始
  ↓
IIS 服務運行？
  ├─ 否 → 啟動 IIS → 重試
  └─ 是 ↓
應用程式池運行？
  ├─ 否 → 啟動應用程式池 → 重試
  └─ 是 ↓
Port 479 監聽？
  ├─ 否 → 檢查 IIS 綁定 → 修復 → 重試
  └─ 是 ↓
本機連線成功？
  ├─ 否 → 查看日誌 → 修復錯誤 → 重試
  └─ 是 ↓
外部連線成功？
  ├─ 否 → 檢查 DNS/防火牆 → 修復 → 重試
  └─ 是 ↓
問題已解決 ?
```

---

## ?? 日誌檢查

### stdout 日誌
```powershell
# 查看最新的 stdout 日誌
Get-ChildItem "Logs\stdout*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Tail 50
```

### Trace.log
```powershell
Get-Content "Logs\Trace.log" -Tail 50
```

### Windows 事件日誌
```powershell
# 應用程式日誌
Get-EventLog -LogName Application -Source "IIS*" -Newest 20

# 系統日誌
Get-EventLog -LogName System -Newest 20 | Where-Object { $_.Source -like "*IIS*" }
```

---

## ?? 測試 URL 清單

### 測試這些 URL 以確定問題範圍

```
1. 根路徑
   https://sunnyvalechback.speechmessage.com.tw:479/
   預期: 重導向到登入頁面

2. 登入頁面
   https://sunnyvalechback.speechmessage.com.tw:479/Home/Login
   預期: 顯示登入頁面

3. 向後相容路徑
   https://sunnyvalechback.speechmessage.com.tw:479/Home/DediationLineLoginView/test
   預期: 重導向到 Dedication 控制器

4. 直接路徑
   https://sunnyvalechback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/test
   預期: 顯示奉獻 LINE 登入頁面

5. 實際 LIFF URL
   https://sunnyvalechback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
   預期: 顯示奉獻 LINE 登入頁面
```

---

## ?? 何時尋求協助

### 立即聯絡技術支援如果:
- [ ] 執行 `iisreset` 後仍然 404
- [ ] 應用程式池無法啟動（立即停止）
- [ ] 日誌中出現嚴重錯誤
- [ ] 所有測試 URL 都失敗
- [ ] 服務器記憶體或 CPU 異常

### 提供以下資訊:
1. 錯誤截圖
2. `診斷DediationLineLoginView-404.bat` 的輸出
3. 最新的 stdout 和 Trace 日誌
4. Windows 事件日誌中的錯誤

---

## ?? 預防措施

### 監控設定
```powershell
# 設定應用程式池為 AlwaysRunning
Set-ItemProperty "IIS:\AppPools\ChurchReport" -name startMode -value AlwaysRunning

# 啟用預載入
Set-ItemProperty "IIS:\Sites\ChurchReport" -name applicationDefaults.preloadEnabled -value $true
```

### 定期檢查
- 每天檢查應用程式池狀態
- 每週檢查 SSL 憑證有效期
- 每月檢查磁碟空間和日誌大小

### 備份配置
```powershell
# 匯出 IIS 配置
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
& "$env:windir\system32\inetsrv\appcmd.exe" list config /config:* > "IIS_Config_Backup_$timestamp.txt"
```

---

## ?? 相關文檔

- **詳細診斷**: `DediationLineLoginView-404錯誤診斷報告.md`
- **程式碼修復**: `DediationLineLoginView登入失敗修復報告.md`
- **向後相容**: `DediationLineLoginView向後相容路由報告.md`
- **測試清單**: `DediationLineLoginView登入失敗測試清單.md`

---

## ?? 常見誤解

### ? 錯誤觀念
"程式碼有問題，所以要修改 Controller"

### ? 正確理解
**這是部署問題，不是程式碼問題**

程式碼檢查結果:
- ? Startup.cs 路由配置正確
- ? DedicationController 方法存在
- ? HomeController 向後相容路由正確
- ? 視圖文件存在
- ? 編譯成功無錯誤

**問題出在**: IIS 配置、服務狀態、網路設定等部署層面

---

## ?? 成功標準

### 修復成功的標誌:
1. ? `iisreset` 後服務正常啟動
2. ? Port 479 處於 LISTENING 狀態
3. ? 本機測試返回 200 或 302
4. ? 外部 URL 可以正常訪問
5. ? LIFF 頁面正常顯示

### 驗證命令:
```powershell
# 一鍵驗證（PowerShell）
$tests = @(
    @{Name="IIS服務"; Test={sc query W3SVC | Select-String "RUNNING"}},
    @{Name="應用程式池"; Test={Import-Module WebAdministration; (Get-WebAppPoolState "ChurchReport") -eq "Started"}},
    @{Name="Port監聽"; Test={netstat -ano | Select-String ":479.*LISTENING"}},
    @{Name="本機連線"; Test={try{Invoke-WebRequest "https://localhost:479/" -UseBasicParsing -TimeoutSec 5; $true}catch{$false}}}
)

foreach($test in $tests) {
    $result = & $test.Test
    $status = if($result){"?"}else{"?"}
    Write-Host "$status $($test.Name)"
}
```

---

**最後提醒**: 
- 先執行 `修復DediationLineLoginView-404.bat`
- 如果失敗，執行 `診斷DediationLineLoginView-404.bat`
- 根據診斷結果查看詳細報告

**技術支援**: tech@sunnyvalech.org
