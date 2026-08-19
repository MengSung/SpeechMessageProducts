# ChurchReport 開發啟動腳本審查報告

本報告針對目前工作區中新增的 ChurchReport PowerShell 開發啟動腳本 `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` 進行審查。

---

## 1. 審查摘要 (Summary)

該 PowerShell 腳本的設計非常嚴謹且完整，成功達成了以下目標：
- **編碼設定**：明確設定了主控台與 .NET 的 UTF-8 輸入輸出編碼，有效防止 Windows PowerShell 5.1 環境下的中文亂碼問題。
- **編譯與啟動**：使用 `dotnet build` 與 `dotnet run`，並透過 `--property:UseAppHost=false` 避免開發過程中因 `apphost.exe` 被鎖定而導致的編譯阻塞。
- **埠與競態處理**：在啟動前先檢查埠是否已被佔用；啟動後以 250ms 為間隔循環偵測埠是否就緒，並在偵測到子程序異常退出時立即中斷，避免無謂的逾時等待。
- **程序樹清理**：利用 `taskkill.exe /T /F` 確保在 Ctrl+C、錯誤或正常結束時，能完整清理 `dotnet run` 產生的所有子程序，避免孤兒程序持續佔用埠。

整體程式碼品質優良，無 Critical 級別的安全性或功能性缺陷。

---

## 2. 審查發現 (Findings)

### Critical
* **無 (No findings)**

---

### Warning

#### Warning 1: 跨平台相容性限制（僅支援 Windows 環境）
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1`
- **程式碼行數**：第 30-42 行
- **現有程式碼**：
  ```powershell
  function Stop-ServerProcessTree {
      ...
      & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
  }
  ```
- **原因說明**：
  `taskkill.exe` 是 Windows 專屬的系統工具。如果開發人員在 macOS 或 Linux 平台上使用 PowerShell 7 (pwsh) 執行此腳本，此清理步驟將會因為找不到 `taskkill.exe` 而失敗，進而導致 `dotnet` 網站程序殘留。
- **建議修改**：
  若此專案未來有跨平台開發的需求，建議加入平台判斷。例如：
  ```powershell
  function Stop-ServerProcessTree {
      param(
          [System.Diagnostics.Process]$Process
      )

      if ($null -eq $Process -or $Process.HasExited) {
          return
      }

      # 判斷是否為 Windows 平台
      $isWindows = $PSVersionTable.Platform -eq 'Windows' -or $env:OS -like "*Windows*" -or $IsWindows
      
      if ($isWindows) {
          & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
      }
      else {
          # Unix-like 平台下強制終止程序及其子程序
          try {
              # 嘗試使用 pkill 終止子程序樹
              & pkill -P $Process.Id 2>$null
              $Process.Kill()
          }
          catch {
              Write-Warning "無法自動清理程序樹，請手動關閉 PID: $($Process.Id)"
          }
      }
  }
  ```

---

### Info

#### Info 1: 建議使用 `$PSScriptRoot` 代替 `$MyInvocation.MyCommand.Path`
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1`
- **程式碼行數**：第 25-26 行
- **現有程式碼**：
  ```powershell
  $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
  $projectDirectory = (Resolve-Path (Join-Path $scriptDirectory '..')).Path
  ```
- **原因說明**：
  自 PowerShell 3.0 起，已內建自動變數 `$PSScriptRoot`。相較於 `$MyInvocation.MyCommand.Path`，`$PSScriptRoot` 在 dot-sourcing（以 `. .\script.ps1` 方式載入）或從其他模組呼叫時更為穩定且簡潔，不易因執行脈絡不同而解析出空值。
- **建議修改**：
  ```powershell
  $scriptDirectory = $PSScriptRoot
  $projectDirectory = (Resolve-Path (Join-Path $scriptDirectory '..')).Path
  ```

---

## 3. 驗證評分 (Validation Report)

依據 CCG 審查規範，針對此腳本的驗證評分如下：

```
VALIDATION REPORT
=================
User Experience: 20/20 - 啟動流程流暢，自動開啟瀏覽器，且 Ctrl+C 清理乾淨，體驗極佳。
Visual Consistency: 20/20 - 輸出訊息搭配了適當的 ForegroundColor，步驟標示清晰。
Accessibility: 20/20 - 參數驗證嚴謹，錯誤訊息明確，並處理了 Windows PowerShell 5.1 的 UTF-8 亂碼問題。
Performance: 20/20 - 使用 --no-build 避免重複編譯，且以 250ms 間隔輪詢埠狀態，啟動反應迅速。
Browser Compatibility: 18/20 - 使用系統預設瀏覽器開啟網址，相容性良好；唯獨 taskkill.exe 限制了非 Windows 平台的執行。

TOTAL SCORE: 98/100

ISSUES FOUND:
- taskkill.exe 限制了非 Windows 平台的程序樹清理能力 (Warning)
- 可改用更現代的 $PSScriptRoot 變數提升路徑解析穩定性 (Info)

RECOMMENDATION: PASS
```
