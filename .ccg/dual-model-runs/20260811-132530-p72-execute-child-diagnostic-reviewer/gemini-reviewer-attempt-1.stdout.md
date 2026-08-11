# UI 審查報告：P7.2 Slice C ExecuteFixture 診斷機制審查

本報告針對 `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1` 與 `docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1` 中未提交的變更進行審查。

---

## 1. 總體評估 (Summary)

本次變更主要為 P7.2 Slice C 引入了 `BootstrapFreshSeed` 模式，並在 ExecuteFixture 子程序（child process）非零結束時，新增了從子程序寫入的 evidence 檔案中提取去識別化診斷分類（`diagnosticCategory`）的機制。

整體設計符合控制面（Control Plane）的無循環、零 mutation 與 fail-closed 安全契約。然而，在 **診斷分類的允許清單（Allowlist）** 中發現了一處關鍵的不一致性，這會導致特定子程序失敗情境下父程序拋出未預期的異常。

---

## 2. 關鍵問題 (Critical Issues)

### 🔴 診斷分類允許清單不一致導致未預期異常
* **檔案路徑**：`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`
* **具體位置**：`Get-StrictSliceCChildFailureDiagnosticCategory` 函式與 `New-HandoffResult` 函式。
* **問題描述**：
  在 `Get-StrictSliceCChildFailureDiagnosticCategory` 中，允許提取的子程序 `reason` 包含 `'live-evidence-incomplete'`：
  ```powershell
  if ($evidence.outcome -cne 'no-go' -or
      $evidence.reason -cnotin @(
          'runtime-failure',
          'cleanup-failure',
          'fixture-precondition-failed',
          'live-evidence-incomplete')) { # 允許 live-evidence-incomplete
      return $null
  }
  ```
  然而，在 `New-HandoffResult` 函式中，對 `$DiagnosticCategory` 的驗證清單卻**未包含** `'live-evidence-incomplete'`：
  ```powershell
  if ($DiagnosticCategory -cnotin @(
          'fixture-precondition-failed',
          'baseline-owner-unavailable',
          ...
          'runtime-failure',
          'cleanup-failure')) { # 缺少 live-evidence-incomplete
      throw 'fresh-fixture-diagnostic-invalid'
  }
  ```
* **影響**：當 ExecuteFixture 子程序寫入的 evidence reason 為 `'live-evidence-incomplete'` 且以非零 exit code 結束時，父程序會提取此分類並傳入 `New-HandoffResult`，進而觸發 `fresh-fixture-diagnostic-invalid` 異常並中斷，無法正確輸出帶有該診斷分類的 `child-process-failed` 結果。

---

## 3. 設計與一致性問題 (Design Issues)

### 🟡 測試案例未覆蓋所有允許的診斷分類
* **檔案路徑**：`docs/scripts/Invoke-Package02Data8ListManagementEvidence.Tests.ps1`
* **問題描述**：
  在測試檔案中，針對子程序失敗診斷分類的測試（`$syntheticNoGoChildResult`）僅使用了 `'runtime-failure'` 進行驗證，未針對 `'live-evidence-incomplete'`、`'cleanup-failure'` 與 `'fixture-precondition-failed'` 等其他允許的分類進行矩陣測試，導致上述的 allowlist 不一致問題未能在單元測試中被檢出。

---

## 4. 改善建議 (Suggestions)

1. **修正 Allowlist 不一致**：
   在 `Invoke-Package02Data8ListManagementEvidence.ps1` 的 `New-HandoffResult` 函式中，將 `'live-evidence-incomplete'` 加入到 `$DiagnosticCategory` 的驗證清單中：
   ```powershell
   if ($DiagnosticCategory -cnotin @(
           'fixture-precondition-failed',
           'baseline-owner-unavailable',
           ...
           'runtime-failure',
           'cleanup-failure',
           'live-evidence-incomplete')) { # 新增此行
       throw 'fresh-fixture-diagnostic-invalid'
   }
   ```

2. **擴充測試矩陣**：
   在 `Invoke-Package02Data8ListManagementEvidence.Tests.ps1` 中，擴充對 `-WriteNoGoEvidence` 的測試，驗證所有四種允許的診斷分類（`runtime-failure`、`cleanup-failure`、`fixture-precondition-failed`、`live-evidence-incomplete`）均能被父程序正確提取並輸出，且任何非允許的分類（或 `go` 狀態）均不會被輸出。

---

## 5. 優秀實作項目 (Positive Notes)

* **嚴格的安全邊界檢查**：`Get-StrictSliceCChildFailureDiagnosticCategory` 實作了極為嚴格的路徑驗證與 `ReparsePoint`（符號連結/接合點）檢查，有效防止了目錄穿越與符號連結劫持攻擊。
* **原子化寫入與讀回驗證**：`Publish-FreshSliceCSeedDescriptor` 採用了原子化寫入（Atomic Write）並立即進行讀回驗證（Read-Back Verification），確保了 `fresh-slice-c-seed.json` 的寫入完整性與編碼合規性（UTF-8 no BOM, CRLF）。
* **確定的資源清理**：在 `finally` 區塊中妥善處理了子進程的強制結束（Taskkill）與暫存目錄的刪除，確保在任何異常情境下均不會殘留孤兒進程或暫存檔案。
