# 審查報告：新增 ChurchReport 可調整組態啟動範例

本報告針對 `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportWithTraceOptions.ps1` 新增腳本，以及 `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` 的既有變更進行程式碼品質、安全性、相容性與設計規範審查。

---

## 驗證報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 19/20 - 設定區定義清晰，參數命名直覺，並提供詳細的註解說明。唯一小缺點是當 buildOnly 設為 true 時，腳本仍會終止執行中的網站程序，這在純編譯情境下可能非預期。
Visual Consistency: 20/20 - 輸出訊息使用一致的 Write-Host 顏色（Magenta, Gray, Cyan, Green, Yellow），與既有腳本風格完全一致。
Accessibility: 19/20 - 完美相容於 Windows PowerShell 5.1 與 PowerShell 7，且設定了 UTF-8 避免亂碼。唯一小缺點是子程序呼叫寫死了 powershell.exe，若在 PowerShell 7 中執行會強制切換回 5.1。
Performance: 20/20 - 採用 TCP 探針與非阻塞的進程等待，沒有不必要的延遲。
Browser Compatibility: 20/20 - 使用 Start-Process -FilePath $Url 呼叫系統預設瀏覽器，相容性佳。

TOTAL SCORE: 98/100

ISSUES FOUND:
- [Info] 子程序呼叫寫死 powershell.exe，在 PowerShell 7 環境下會強制切換回 5.1。
- [Info] 在 buildOnly 模式下仍會執行 Stop-ExistingChurchReportForPort 終止既有網站程序。

RECOMMENDATION: PASS
```

---

## 審查發現分類

### Critical (嚴重問題)
* **無**。未發現任何會導致系統崩潰、安全性漏洞或繞過 Release 防線的嚴重問題。

### Warning (警告事項)
* **無**。環境變數還原、程序停止與防誤殺邏輯均實作得非常安全且符合預期。

### Info (提示資訊)

#### 1. 子程序 PowerShell 版本切換
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportWithTraceOptions.ps1` (第 214 行)
* **說明**：腳本中寫死使用 `powershell.exe` 啟動子腳本。如果使用者是在 PowerShell 7 (`pwsh.exe`) 中執行此腳本，這會強制子程序切換回 Windows PowerShell 5.1。
* **建議**：可改用當前 PowerShell 進程的執行檔路徑，例如 `(Get-Process -Id $PID).Path`，以保持執行版本的一致性。
  ```powershell
  $psCmd = (Get-Process -Id $PID).Path
  & $psCmd @starterArguments
  ```

#### 2. `buildOnly` 情境下的程序終止
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportWithTraceOptions.ps1` (第 165-172 行)
* **說明**：當 `$buildOnly` 設為 `$true` 時，腳本仍會在編譯前終止既有的 ChurchReport 網站程序。雖然這符合「編譯與啟動前」的字面需求，但若使用者僅想進行編譯而不打算啟動，終止執行中的網站可能會造成不便。
* **建議**：可考慮將 `Stop-ExistingChurchReportForPort` 的呼叫移至 `if (-not $buildOnly)` 區塊內，僅在確實需要啟動網站時才釋放連接埠。

---

## 審查細項說明

### 1. PowerShell 5.1／7 語法與 UTF-8 輸出相容性
* **編碼處理**：腳本開頭明確設定了 `[Console]::InputEncoding`、`[Console]::OutputEncoding` 與 `$OutputEncoding` 為 UTF-8，這能有效避免 Windows PowerShell 5.1 與 .NET 子程序之間因為預設 ANSI 編碼不同而產生的中文亂碼問題。
* **語法相容性**：腳本中使用的 `Get-CimInstance`、`Get-NetTCPConnection` 等 Cmdlet 在 PowerShell 5.1 與 7 中均能正常運作，且未使用任何僅限於特定版本的語法。

### 2. 安全程序停止與防誤殺機制
* **連接埠與 PID 關聯**：透過 `Get-NetTCPConnection -LocalPort $Port` 取得占用該連接埠的 PID，並使用 `Get-CimInstance Win32_Process` 取得該程序的 `CommandLine`。
* **防誤殺邏輯**：腳本會嚴格檢查 `CommandLine` 是否包含專案目錄路徑或專案名稱 `SpeechMessageProducts.ChurchReport`。如果無法讀取命令列（例如權限不足）或確認不屬於 ChurchReport，腳本會拋出異常並拒絕執行 `taskkill`。這能完全避免誤殺其他無關的系統服務或應用程式。
* **資源清理**：使用 `taskkill.exe /PID $Process.Id /T /F` 確保 `dotnet run` 啟動的子程序樹被完整清理，避免留下孤兒網站程序持續占用連接埠。

### 3. Debug／Release 編譯與 DiagnosticsTrace 覆寫契約
* **環境變數隔離**：透過 `[Environment]::GetEnvironmentVariable` 備份，並在 `finally` 區塊中還原，確保 `DiagnosticsTrace__Enabled` 與 `DiagnosticsTrace__Directory` 僅在當前腳本與啟動的子程序中生效，不會污染系統或使用者的永久環境變數。
* **Release 編譯期防線**：
  * 經確認，`Program.cs` 中使用 `#if DEBUG` 條件編譯。在 Release 組態下，程式碼會強制呼叫 `DiagnosticTraceOptions.CreateDisabled`，完全無視任何環境變數或設定檔。
  * 新腳本中亦加入了對應的警告提示：
    ```powershell
    if ($configuration -eq 'Release' -and $diagnosticTraceEnabled) {
        Write-Warning 'Release 組態具有編譯期停用防線；即使設定 true，正式組態仍不會建立三種檔案 Trace。'
    }
    ```
  * 這確保了 Release 的產品編譯期 fail-closed 防線不會被腳本繞過，符合安全設計契約。

### 4. 既有變更保護
* 經比對，`Start-ChurchReportDevelopment.ps1` 的既有變更（如新增 `Stop-ExistingChurchReportForPort` 函數、加入 `--property:UseAppHost=false` 避免 apphost 鎖定、以及設定 `$env:ASPNETCORE_URLS`）均被完整保留，且新腳本主要是透過環境變數與參數傳遞來呼叫既有腳本，並未破壞既有功能。
