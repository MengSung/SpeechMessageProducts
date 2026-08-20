# 審查報告：新增 ChurchReport 可調整組態啟動範例

本報告針對工作樹中未提交的變更進行審查，重點關注新腳本 `Start-ChurchReportWithTraceOptions.ps1` 的實作安全性、與既有腳本 `Start-ChurchReportDevelopment.ps1` 的相容性，以及是否符合產品的編譯期安全防線。

---

## 驗證報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 19/20 - 設定區參數清晰直覺，並有詳細的註解說明。唯一小缺點是 buildOnly 時也會終止既有程序。
Visual Consistency: 20/20 - 輸出訊息使用一致的 Write-Host 顏色風格，與既有腳本保持高度一致。
Accessibility: 19/20 - 完美相容於 Windows PowerShell 5.1 與 PowerShell 7，且設定了 UTF-8 避免亂碼。
Performance: 20/20 - 採用 TCP 探針與非阻塞的進程等待，無不必要的延遲。
Browser Compatibility: 20/20 - 使用系統預設瀏覽器啟動，相容性佳。

TOTAL SCORE: 98/100

ISSUES FOUND:
- [Info] 子程序 PowerShell 版本切換：寫死 powershell.exe 會導致在 PowerShell 7 執行時強制降級至 5.1 執行子腳本。
- [Info] buildOnly 情境下的程序終止：在僅編譯不啟動的情境下，仍會終止執行中的網站程序。

RECOMMENDATION: PASS
```

---

## 審查發現分類 (Findings Classification)

### Critical (嚴重問題)
* **無**。未發現任何會導致系統崩潰、安全性漏洞或繞過 Release 防線的嚴重問題。

### Warning (警告事項)
* **無**。環境變數還原、程序停止與防誤殺邏輯均實作得非常安全且符合預期。

### Info (提示資訊)

#### 1. 子程序 PowerShell 版本切換
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportWithTraceOptions.ps1` (第 214 行)
* **說明**：腳本中寫死使用 `powershell.exe` 啟動子腳本。如果使用者是在 PowerShell 7 (`pwsh.exe`) 中執行此腳本，這會強制子程序切換回 Windows PowerShell 5.1。
* **建議**：可改用當前 PowerShell 進程的執行檔路徑，以保持執行版本的一致性。例如：
  ```powershell
  $psCmd = (Get-Process -Id $PID).Path
  & $psCmd @starterArguments
  ```

#### 2. `buildOnly` 情境下的程序終止
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportWithTraceOptions.ps1` (第 165-172 行)
* **說明**：當 `$buildOnly` 設為 `$true` 時，腳本仍會在編譯前終止既有的 ChurchReport 網站程序。雖然這符合「編譯與啟動前」的字面需求，但若使用者僅想進行編譯而不打算啟動，終止執行中的網站可能會造成不便。
* **建議**：可考慮將 `Stop-ExistingChurchReportForPort` 的呼叫移至 `if (-not $buildOnly)` 區塊內，僅在確實需要啟動網站時才釋放連接埠。

---

## 需求符合性檢查 (Requirements Verification)

1. **PowerShell 5.1／7 語法與 UTF-8 輸出相容性**：
   * **通過**。兩個腳本均明確設定了 `[Console]::InputEncoding`、`[Console]::OutputEncoding` 與 `$OutputEncoding` 為 UTF-8 (No BOM)，有效防止中文亂碼。
   * **通過**。使用 `Get-CimInstance` 代替已在 PowerShell 7 中被淘汰的 `Get-WmiObject`，確保了跨版本的相容性。

2. **程序停止、PID／連接埠判斷、taskkill、逾時與資源清理安全性**：
   * **通過**。使用 `Get-NetTCPConnection` 獲取監聽連接埠的 PID，並透過 `Get-CimInstance Win32_Process` 查詢 `CommandLine`。
   * **通過**。嚴格限制只有當命令列中包含專案路徑或專案名稱時才判定為 ChurchReport 程序；若有任何無法確認的程序占用該連接埠，則拋出異常拒絕執行，完全避免誤殺其他服務。
   * **通過**。使用 `taskkill.exe /T /F` 強制終止程序樹，避免留下孤兒程序，並設有 10 秒的逾時等待機制。

3. **Debug／Release 編譯與 DiagnosticsTrace 覆寫契約**：
   * **通過**。腳本透過設定進程級環境變數 `DiagnosticsTrace__Enabled` 與 `DiagnosticsTrace__Directory` 來覆寫設定，並在 `finally` 區塊中安全還原，不污染全域環境變數。
   * **通過**。經核實，`Program.cs` 在 `#else` (Release) 區塊中強制呼叫 `DiagnosticTraceOptions.CreateDisabled`，因此 Release 的產品編譯期 fail-closed 防線非常穩固，無法被腳本或環境變數繞過。

4. **既有變更保護**：
   * **通過**。`Start-ChurchReportDevelopment.ps1` 的既有變更（如新增的 `Stop-ExistingChurchReportForPort` 函數、`--property:UseAppHost=false` 參數等）均未被破壞，新功能主要由新腳本 `Start-ChurchReportWithTraceOptions.ps1` 提供。
