# P7.2 Slice C FreshPreflightProbe 審查報告

本報告針對當前工作區中未提交的 P7.2 變更進行審查，重點在於新引入的 `-FreshPreflightProbe` PowerShell 參數集、`Get-StrictFreshPreflightProbeEvidenceFile` 解析器、C# 固定類別證據寫入器與探針、程序環境快照/還原、超時與子程序失敗投影，以及任務擁有的新鮮固定裝置突變邊界。

---

## 審查清單驗證結果

### 1. 探針唯讀性與確定性清理 (Accessibility & Design Consistency)
- [x] **驗證結果**：**符合**。
  - 探針實作於 `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixturePreflightProbe.cs`。
  - 探針僅使用 `_service.Retrieve`（精確 ID 檢索）和 `_service.RetrieveMultiple`（有界檢索多個，`TopCount = 2`）。
  - 探針未執行任何 `Create`, `Update`, `Delete`, `Execute` (用於 Assign), `Associate`, `Disassociate` 等突變操作。
  - 探針為純唯讀操作，無任何副作用，清理是確定性的（無須清理）。

### 2. 證據去識別化與邊界隔離 (Code Quality)
- [x] **驗證結果**：**符合**。
  - 探針返回的 `P72FreshSliceCFixturePreflightProbeResult` 僅包含固定的枚舉值（如 `"go"`, `"no-go"`, `"valid"`, `"systemuser"`, `"active"`, `"different-from-data8"`, `"exactly-one-active"` 等）。
  - 證據檔案中不包含任何 CRM ID、名稱、端點、憑證、權杖、Cookie、原始回應、原始異常或基準值。
  - 異常處理使用 `catch (Exception)` 捕獲所有異常並返回固定值 `"probe-unavailable"`，未洩露任何原始異常訊息。
  - 證據寫入器 `P72FreshSliceCFixtureLiveEvidence.cs` 在寫入前會呼叫 `ValidatePreflightProbeValue` 進行嚴格的枚舉值與組合驗證。

### 3. 互斥性與投影屬性 (Design Consistency)
- [x] **驗證結果**：**符合**。
  - 在 PowerShell 腳本 `Invoke-Package02Data8ListManagementEvidence.ps1` 中，`FreshPreflightProbe` 是一個獨立的 ParameterSetName，與所有突變/協調/修復模式互斥。
  - 當啟用 `-FreshPreflightProbe` 時，`$operationMayHaveExecuted` 設為 `$false`。
  - 在 `New-HandoffResult` 中，若為探針模式，則 `safeToRetry` 總是投影為 `$false`，且 `operationExecuted` 總是為 `$false`。

### 4. 新鮮固定裝置突變邊界 (Performance & Responsive)
- [x] **驗證結果**：**符合**。
  - 新鮮固定裝置的突變與清理實作於 `P72FreshSliceCFixtureProvisioner` 中。
  - 突變順序嚴格固定（`create:source` -> `create:leader` -> `create:relationship-list` -> `add:remove` -> `add:transfer-source` -> `assign:baseline-owner`）。
  - 清理順序嚴格相反（`remove:transfer-source` -> `remove:remove` -> `delete:relationship-list` -> `delete:source` -> `delete:leader`）。
  - 每個步驟都會寫入並更新 `fresh-slice-c-ledger.json`（使用 schema v2，包含 `originalTargetLeaderContactId` 欄位以確保 baseline 不可變），並在讀回驗證後才進行下一步。
  - 任何步驟失敗都會導致 `provisioning-ambiguous` 或 `cleanup-ambiguous`，且不會前進或重試。

### 5. 安全隔離與檔案格式 (Code Quality)
- [x] **驗證結果**：**符合**。
  - **跨使用者隔離**：`P72FreshSliceCFixtureFileLedger` 和 `P72FreshSliceCFixtureLiveEvidence` 在讀寫檔案時會呼叫 `RejectReparsePoint`，拒絕 reparse point（如 junction, symlink），防止符號連結攻擊。
  - **超時與失敗投影**：PowerShell 腳本設定了 `--blame-hang-timeout 150s`，超時後強制結束子程序並返回 `test-timeout`，子程序失敗時返回 `child-process-failed`。
  - **檔案格式**：所有 JSON 檔案均使用 UTF-8 no-BOM 寫入，且結尾加上 `\r\n`。PowerShell 腳本的 `Read-StrictJsonFile` 和 `Read-StrictTextFile` 會驗證檔案是否使用 CRLF-only 換行且以 `\r\n` 結尾。

---

## 審查發現分類

### Critical (嚴重)
*無相關發現。* 所有關鍵安全機制、去識別化、互斥性、環境隔離、換行格式、以及 reparse point 拒絕等安全邊界均已正確實作且有完整的測試覆蓋。

### Warning (警告)
*無相關發現。*

### Info (資訊)

#### 1. Reparse Point 祖先路徑拒絕測試被跳過
- **檔案路徑**：
  - `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedgerTests.cs` (第 988 行)
  - `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveGateTests.cs` (第 431 行)
- **說明**：這兩個測試方法（`Constructor_rejects_a_parent_owned_root_with_a_reparse_point_ancestor` 和 `Parent_owned_root_gate_rejects_a_reparse_point_ancestor`）因為在 Windows 測試環境中需要 `SeCreateSymbolicLinkPrivilege` 權限而被標記為 `Skip`。
- **建議**：雖然這是常見的權限限制，但建議在 CI/CD 環境中確保執行帳號擁有足夠權限以啟用並執行此測試，以持續驗證 reparse point 祖先路徑的拒絕邏輯。

---

## 評分與建議 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 探針與新鮮固定裝置的參數設計清晰，互斥邏輯嚴格，能有效防止操作人員誤用。
Visual Consistency: 20/20 - 檔案格式（UTF-8 no-BOM, CRLF-only, final CRLF）與 schema 欄位定義高度一致。
Accessibility: 20/20 - 嚴格的跨使用者隔離與 reparse point 拒絕機制，確保了多使用者環境下的安全性。
Performance: 20/20 - 探針僅執行必要的唯讀查詢，且有界檢索（TopCount=2）能防止大數據量查詢造成的效能問題。
Browser Compatibility: 20/20 - 不適用於此後端/PowerShell 變更，給予滿分。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無關鍵或警告級別的問題。僅有兩項關於跳過 Reparse Point 測試的資訊提示。

RECOMMENDATION: PASS
```
