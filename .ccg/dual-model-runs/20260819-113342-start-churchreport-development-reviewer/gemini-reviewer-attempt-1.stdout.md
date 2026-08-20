# 程式碼審查報告：Start-ChurchReportDevelopment.ps1

本報告針對 `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` 進行審查。該腳本旨在提供開發人員一鍵啟動 ChurchReport 開發環境的工具，包含 UTF-8 環境設定、dotnet 編譯、啟動網站、等待埠就緒、開啟瀏覽器以及在結束時清理程序樹。

---

## 審查摘要 (Summary)

整體而言，該腳本結構清晰、參數驗證完整，且考慮到了 Windows PowerShell 5.1 與 PowerShell 7 的編碼相容性，並實作了埠佔用檢查與程序樹清理機制。然而，在**跨平台相容性**、**參數轉義安全**以及**健壯性（Robustness）**上仍有改善空間。

---

## 審查發現 (Findings)

### 🔴 Critical
*無發現 (No findings)*

---

### 🟡 Warning

#### 1. `Stop-ServerProcessTree` 中 `taskkill.exe` 的跨平台相容性與錯誤處理問題
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 30-42 行)
* **問題說明**: 
  1. 腳本直接呼叫 Windows 特有的 `taskkill.exe`。若開發人員在 Linux 或 macOS 的 PowerShell 7 環境下執行此腳本，會因為找不到 `taskkill.exe` 而拋出 `CommandNotFoundException`。
  2. 由於腳本頂部設定了 `$ErrorActionPreference = 'Stop'`，如果在 `finally` 區塊中呼叫 `Stop-ServerProcessTree` 時拋出未捕獲的異常，該異常會中斷 `finally` 的執行，且可能會掩蓋 `try` 區塊中原本的真實錯誤。
* **建議修正**: 
  在 `Stop-ServerProcessTree` 內部使用 `try-catch` 包裹，並在非 Windows 平台或 `taskkill` 失敗時，提供 `Stop-Process` 作為備用方案。例如：
  ```powershell
  function Stop-ServerProcessTree {
      param(
          [System.Diagnostics.Process]$Process
      )

      if ($null -eq $Process) {
          return
      }

      try {
          if ($Process.HasExited) {
              return
          }
          # 判斷是否為 Windows 系統
          if ($IsWindows -or $env:OS -like "*Windows*") {
              & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
          } else {
              Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
          }
      }
      catch {
          # 備用清理方案，避免拋出異常中斷 finally 流程
          try {
              Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
          } catch {}
      }
  }
  ```

#### 2. `Start-Process` 參數中的專案路徑手動加雙引號可能導致轉義錯誤
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 113 行)
* **問題說明**: 
  在 `$serverArguments` 中，專案路徑被寫為 `('"{0}"' -f $projectPath)`。這會產生一個帶有實體雙引號的字串（例如 `"D:\path\to\project.csproj"`）。當這個字串作為陣列元素傳給 `Start-Process -ArgumentList` 時，PowerShell 會嘗試對其進行轉義，這在不同的 PowerShell 版本（特別是 Windows PowerShell 5.1 與 PowerShell 7 之間）可能會導致傳遞給 `dotnet` 的參數變成 `""D:\path\to\project.csproj""`，進而導致 `dotnet` 報錯找不到專案檔。
* **建議修正**: 
  直接傳遞 `$projectPath`，不需要手動加上雙引號。`Start-Process` 的 `-ArgumentList` 接受陣列，會自動為含有空白或特殊字元的參數加上適當的引號與轉義。
  ```powershell
  # 修改前
  ('"{0}"' -f $projectPath)

  # 修改後
  $projectPath
  ```

---

### 🔵 Info

#### 1. 建議使用 `$PSScriptRoot` 代替 `$MyInvocation.MyCommand.Path`
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1` (第 25-26 行)
* **問題說明**: 
  腳本使用 `$MyInvocation.MyCommand.Path` 來獲取目前腳本的目錄。雖然這在直接執行腳本時有效，但在某些特殊調用場景下（例如 dot-sourcing 或透過 pipeline 執行）可能會返回空值。自 PowerShell 3.0 起，推薦使用自動變數 `$PSScriptRoot`，它在所有執行方式下都能穩定返回腳本所在的目錄。
* **建議修正**:
  將路徑解析修改為：
  ```powershell
  $projectDirectory = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
  ```

---

## 評分與驗證報告 (Validation Report)

```
VALIDATION REPORT
=================
User Experience: 18/20 - 啟動流程流暢，有自動開啟瀏覽器與埠佔用提示，體驗良好。
Visual Consistency: 19/20 - 輸出訊息使用 Console 顏色區分步驟，視覺提示清晰。
Accessibility: 20/20 - 設定了 UTF-8 編碼，避免了中文環境下的亂碼問題。
Performance: 18/20 - 使用 --no-build 避免重複編譯，並有 250ms 的 polling 埠檢查，效能良好。
Browser Compatibility: 18/20 - 使用系統預設瀏覽器開啟，相容性高。

TOTAL SCORE: 93/100

ISSUES FOUND:
- taskkill.exe 在非 Windows 平台下會失效且可能在 finally 中拋出未捕獲異常。
- 手動為專案路徑加上雙引號可能導致 Start-Process 轉義錯誤。
- 使用舊式的 $MyInvocation 獲取腳本路徑，建議改用 $PSScriptRoot。

RECOMMENDATION: PASS (建議在後續迭代中修正上述 Warning 項目以提升腳本健壯性)
```
