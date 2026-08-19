# ChurchReport 開發啟動腳本審查報告

本報告針對工作區中新增的 `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` PowerShell 開發啟動腳本進行程式碼審查。

---

## 審查摘要 (Summary)
此腳本的整體設計非常優良，完整考慮了開發人員在啟動本地開發環境時的各種需求。腳本成功實現了：
1. **UTF-8 編碼設定**：避免了 Windows 環境下常見的中文亂碼問題。
2. **編譯與啟動優化**：使用 `--property:UseAppHost=false` 避免 apphost 檔案鎖定衝突，並使用 `--no-build` 避免重複編譯。
3. **埠號衝突與啟動檢查**：在啟動前主動檢查埠號是否被佔用，並在啟動時以輪詢（Polling）方式等待埠號可用，同時監控程序是否提早結束，避免無謂的等待。
4. **程序樹清理**：在 `finally` 區塊中使用 `taskkill.exe /T /F` 確保不論是正常結束、Ctrl+C 還是異常中斷，都能乾淨地清理 `dotnet run` 產生的子程序樹，防止孤兒程序佔用埠號。

以下為針對該腳本的具體審查意見與改進建議。

---

## 審查結果分類 (Findings)

### Critical (嚴重)
* **無 (No findings)**：未發現會導致系統崩潰、安全性漏洞或核心功能失效的嚴重問題。

---

### Warning (警告)

#### 1. `$MyInvocation.MyCommand.Path` 在特定執行情境下可能為空
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 25 行)
* **程式碼**：
  ```powershell
  $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
  ```
* **理據**：在某些執行方式下（例如 dot-source 執行 `. .\Start-ChurchReportDevelopment.ps1`、透過某些 IDE 整合終端機執行，或使用 `Invoke-Expression` 時），`$MyInvocation.MyCommand.Path` 可能會返回空值，導致後續的 `$projectDirectory` 解析失敗並拋出異常。
* **建議**：改用 PowerShell 3.0+ 內建的自動變數 `$PSScriptRoot`，這在所有標準執行情境下都更為穩定且安全。
  ```powershell
  $scriptDirectory = $PSScriptRoot
  ```

#### 2. 全域編碼設定在 `try` 區塊之外，可能導致未捕獲的異常
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 21-23 行)
* **程式碼**：
  ```powershell
  [Console]::InputEncoding = $utf8
  [Console]::OutputEncoding = $utf8
  $OutputEncoding = $utf8
  ```
* **理據**：在某些非標準的 PowerShell Host（例如某些 CI/CD 環境或嵌入式主控台）中，修改 `[Console]::InputEncoding` 可能會拋出不支援的異常。由於此時尚未進入 `try` 區塊，且 `$ErrorActionPreference = 'Stop'` 已生效，這會導致腳本直接中斷，且無法透過 `catch` 輸出友好的錯誤提示。
* **建議**：將這三行編碼設定移入 `try` 區塊內，以確保任何初始化階段的異常都能被統一捕獲並妥善處理。

---

### Info (提示)

#### 1. `exit 1` 對互動式主控台的影響
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 158 行)
* **程式碼**：
  ```powershell
  catch {
      Write-Error $_
      exit 1
  }
  ```
* **理據**：如果開發人員是在現有的 PowerShell 視窗中以 dot-source 方式執行此腳本，當觸發 `exit 1` 時，會直接關閉整個 PowerShell 視窗（視窗會直接消失），這可能會影響開發體驗。
* **建議**：若要避免此問題，可以改用 `return` 結束腳本，或者在腳本開頭檢測是否為 dot-sourced。但若此腳本主要設計為透過雙擊或獨立進程執行，則維持 `exit 1` 亦屬合理。

#### 2. `Wait-Process` 潛在的 PID 不存在錯誤
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 154 行)
* **程式碼**：
  ```powershell
  Wait-Process -Id $serverProcess.Id
  ```
* **理據**：若 `dotnet` 程序在執行到 `Wait-Process` 前的極短瞬間剛好結束，`Wait-Process` 會因為找不到該 PID 而拋出錯誤。
* **建議**：可加上 `-ErrorAction SilentlyContinue` 以提高強健性：
  ```powershell
  Wait-Process -Id $serverProcess.Id -ErrorAction SilentlyContinue
  ```

---

## 結論與建議 (Decision)
本腳本的編寫品質相當高，充分考慮了 PowerShell 5.1 與 7 的相容性，並針對 `dotnet run` 的程序樹清理做了正確的處理。

**建議：PASS（通過）**，但建議在後續維護中採納上述 **Warning** 的修改（特別是改用 `$PSScriptRoot`），以提升腳本在不同開發環境下的強健性。
