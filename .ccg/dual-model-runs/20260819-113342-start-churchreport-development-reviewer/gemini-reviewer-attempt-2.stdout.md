# ChurchReport 開發啟動腳本審查報告

本報告針對新增的 PowerShell 開發啟動腳本 `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` 進行程式碼品質、相容性、錯誤處理與程序清理機制的審查。

---

## 1. 審查摘要 (Summary)
新增的 `Start-ChurchReportDevelopment.ps1` 腳本整體設計優良，具備完善的防禦性程式設計（Defensive Programming）。腳本成功實作了：
- **UTF-8 編碼強制設定**：避免 Windows PowerShell 5.1 與 .NET 輸出中文亂碼。
- **埠佔用預檢**：避免多個執行個體衝突。
- **非同步埠就緒偵測**：避免瀏覽器在網站尚未完全啟動前開啟。
- **程序樹清理機制**：利用 `taskkill /T /F` 確保 `dotnet run` 產生的子程序在 Ctrl+C 或錯誤發生時能被徹底清理，避免埠被持續佔用。

然而，腳本在**參數引號處理**與**跨平台相容性**上存在些許改進空間。

---

## 2. 審查結果分級 (Findings)

### Critical (嚴重)
* **無 (No findings)**：未發現會導致系統崩潰、安全性漏洞或核心功能失效的嚴重問題。

### Warning (警告)

#### 1. 參數手動加引號導致 `dotnet` 解析失敗風險
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 113 行)
* **程式碼**：
  ```powershell
  ('--project', ('"{0}"' -f $projectPath))
  ```
* **原因分析**：
  當使用 `Start-Process -ArgumentList $serverArguments` 且傳遞的是陣列時，PowerShell 會自動為包含空格的參數加上雙引號。如果在此處手動使用 `'"..."'` 包裝路徑，PowerShell 在將參數傳遞給 Windows API 時會將其轉義為 `\"path\"`。這在路徑中包含空格或在特定 PowerShell 版本中，會導致 `dotnet` 無法正確解析專案路徑，拋出 `MSB1009: Project file does not exist.` 錯誤。
* **建議修正**：
  直接傳遞 `$projectPath`，讓 PowerShell 自動處理引號：
  ```powershell
  $serverArguments = @(
      'run'
      '--no-launch-profile'
      '--no-build'
      '--configuration'
      $Configuration
      '--property:UseAppHost=false'
      '--project'
      $projectPath
  )
  ```

#### 2. `taskkill.exe` 跨平台相容性限制
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 41 行)
* **程式碼**：
  ```powershell
  & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
  ```
* **原因分析**：
  `taskkill.exe` 是 Windows 系統特有的工具。如果開發人員在 Linux 或 macOS 上使用 PowerShell 7 (`pwsh`) 執行此腳本，將會因為找不到 `taskkill.exe` 而無法清理程序樹，導致 `dotnet` 子程序殘留並持續佔用連接埠。
* **建議修正**：
  若此專案未來有跨平台開發需求，建議加入作業系統判斷：
  ```powershell
  if ($IsWindows -or $env:OS -like "*Windows*") {
      & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
  } else {
      # 非 Windows 系統的清理邏輯，例如使用 kill
      # 或是透過 shell 呼叫 kill -9 -$Process.Id (若為 process group leader)
  }
  ```

---

### Info (提示)

#### 1. `localhost` 在雙疊網路環境下的解析延遲
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 50-60 行)
* **原因分析**：
  在 `Test-TcpPortReady` 中使用 `TcpClient.ConnectAsync` 連接 `localhost` 時，在啟用 IPv6 的系統上，`localhost` 可能會優先解析為 `::1`。如果 `dotnet` 僅監聽了 IPv4 的 `127.0.0.1`，可能會導致連線嘗試逾時或產生不必要的延遲。
* **建議**：
  這通常不會造成嚴重問題，但若在開發環境中發現啟動偵測緩慢，可考慮將預設 URL 改為 `http://127.0.0.1:5000/`。

---

## 3. 建議與改進方案 (Suggestions)
除了上述 Warning 的修正外，建議在 `finally` 區塊中加入更明確的日誌輸出，讓使用者知道清理程序正在執行：
```powershell
finally {
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        Write-Host '正在清理 ChurchReport 網站程序...' -ForegroundColor Yellow
        Stop-ServerProcessTree -Process $serverProcess
        Write-Host '清理完成。' -ForegroundColor Green
    }
}
```

---

## 4. 優秀設計點 (Positive Notes)
1. **嚴格模式與錯誤中斷**：使用 `Set-StrictMode -Version Latest` 與 `$ErrorActionPreference = 'Stop'`，確保腳本在遇到未定義變數或錯誤時能立即中斷，避免帶病執行。
2. **無 BOM UTF-8 設定**：主動設定 `[Console]::OutputEncoding`，徹底解決了 Windows 環境下 PowerShell 執行 .NET 應用程式時常見的中文亂碼問題。
3. **啟動中程序狀態檢查**：在等待埠就緒的迴圈中，同時檢查了 `$serverProcess.HasExited`。這是一個非常優秀的設計，能讓腳本在 `dotnet` 啟動即崩潰時立即報錯退出，而不需要白白等待 60 秒逾時。
4. **避免 apphost 鎖定**：在編譯與執行時皆加上 `--property:UseAppHost=false`，有效避免了開發過程中因 `apphost.exe` 被佔用而導致編譯失敗的問題。
