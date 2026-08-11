## P7.2 Slice C fresh-fixture 最終審查報告（Claude Reviewer）

### 審查範圍
`git diff` 涵蓋 9 個檔案（+2084/−127）：C# ledger/evidence/gate/測試四檔、PowerShell 主控制面與兩個合約測試檔、任務延續紀錄。已逐檔比對變更前後邏輯，並交叉核對 parent（PowerShell）與 child（C#）在 ledger root 建立責任轉移、診斷分類 allowlist、CRLF/UTF-8 邊界三處的一致性。

---

### Critical 🔴
無。未發現會導致跨使用者/profile 洩漏、未授權 CRM 寫入、reparse 逃逸、或 no-mutation 保證被破壞的缺陷。

---

### Warning 🟡

**1. 診斷分類 allowlist 重複維護於三處，存在漂移風險**
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1:281-292`（`New-HandoffResult` 內的 12 項分類檢查）
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1` 內 `Get-StrictFreshFixtureChildFailureDiagnosticCategory`（約 1290-1301 行）的同一份 12 項清單
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveEvidence.cs:73-86`（`ProvisionDiagnosticCategories`）

三份清單目前逐字相同，但沒有單一事實來源。任何未來新增 diagnostic category 若只更新其中一到兩處，行為是「fail closed 靜默省略」（`diagnosticCategory` 欄位消失，仍保持 `child-process-failed`/`safeToRetry=false`），不會造成安全問題，但會讓新分類永遠無法被 parent 觀察到，且錯誤不易被發現（無例外、無測試失敗信號，除非合約測試逐一列舉新分類）。建議：三處改為由單一 allowlist（例如 C# 側輸出、PS 側讀入同一個常數來源，或至少加一條契約測試比對三份清單雜湊/內容相等）。

**2. Reparse-point ancestor 防護的兩個回歸測試在目前 CI/測試機權限下永遠 Skip**
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedgerTests.cs:988`
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveGateTests.cs:332`

兩者都因缺少 `SeCreateSymbolicLinkPrivilege` 而 Skip。程式碼本身（`P72FreshSliceCFixtureOwnedPathGuard.ThrowIfReparsePointPathOrAncestor`，`P72FreshSliceCFixtureFileLedger.cs:643`）邏輯正確且被其餘非特權測試間接覆蓋 leaf 層防護，但「祖先目錄為 reparse point」這條關鍵防線在一般測試環境中完全無自動化驗證。與 Gemini 審查意見一致。建議：在具備該權限的測試機/排程上額外執行這兩個 Fact，或改用可注入的檔案系統抽象進行單元測試，避免依賴 OS 權限。

---

### Info 🟢

**1. `TryWriteDiagnostic` 的空 catch 是刻意設計，符合 fail-closed 原則**
`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveEvidence.cs:138-142` 對次要 I/O 失敗完全吞噬例外。這是正確選擇（次要診斷寫入失敗不得掩蓋或竄改既有 no-go 結果），但代表若磁碟/權限問題導致診斷永遠寫不出，將無聲消失、難以排查。建議僅在有內部安全 trace channel（非 stdout/evidence）時才考慮補記錄，非必要。

**2. `ValidateExistingLedgerForReplacement` 前存在極短暫、已知的同使用者 TOCTOU 視窗**
`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedger.cs:186-188` 的 `RejectReparsePoint(_path)` 與其後 `ValidateExistingLedgerForReplacement`（375 行起）內的 `File.ReadAllBytes(_path)` 之間，理論上同一使用者的另一個惡意 process 可搶先替換檔案。文件註解已明確承認此為「TOCTOU 視窗限制而非消除」的同使用者信任模型殘留風險，屬於可接受設計權衡，非本次引入的新缺陷。

**3. `EnsureOwnedRoot` 行為變更（child 不再自動建立 root）已與 parent 端變更配對正確**
`P72FreshSliceCFixtureFileLedger.cs:548-556` 改為僅驗證既有目錄、不再 `Directory.CreateDirectory`。已確認 `Invoke-Package02Data8ListManagementEvidence.ps1:1503-1518` 的 `Get-CurrentUserFreshFixtureControlPlaneRoots` 在 child 執行前就會建立 `ledgerRoot`，且新增的兩個 gate 回歸測試（`Fresh_provision_gate_rejects_a_nonexistent_parent_owned_ledger_root_without_creating_it` 等，`P72FreshSliceCFixtureLiveGateTests.cs:120-215`）驗證了「root 不存在時 child 不得建立」。此變更内部一致，未發現斷裂。

---

### No-Mutation 保證確認（baseline-owner-unavailable 分支）

**確認：保證成立。** 依據：

1. `LivePackage02Data8ListManagementFreshFixtureTests.cs:79-106` 顯示 `P72FreshSliceCFixtureProvisioner.Provision` 在回傳 no-go 時，`ledger`（`P72FreshSliceCFixtureFileLedger`）的 `Persist` 只有在每個已證明 stage 完成後才呼叫；`baseline-owner-unavailable` 依 `.trellis/tasks/.../p7.2-slice-c-continuation-2026-08-10.md` 記錄，係在第一次 `Persist`（`preflight-proven`）與任何 CRM `Create`/`Assign` 之前即返回，本次 diff 未變更此判斷順序。
2. 本次新增的 `TryWriteDiagnostic` 呼叫（`LivePackage02Data8ListManagementFreshFixtureTests.cs:143-152`）只在既有 `outcome == "no-go"` 判定**之後**執行，寫入內容僅為 allowlist 分類字串，不含 CRM ID，且其寫入失敗會被吞噬（`P72FreshSliceCFixtureLiveEvidence.cs:138-142`），不影響已決定的 `outcome`/`reason`/`safeToRetry`。
3. Parent 端讀取分類（`Get-StrictFreshFixtureChildFailureDiagnosticCategory`）僅在 child ExitCode 非零後被呼叫，其結果只會被塞進 `diagnosticCategory` 展示欄位（`New-HandoffResult` 內固定於 `reason='child-process-failed'`、`safeToRetry=false` 之下），不會觸發 descriptor 發布、cleanup 派送或重試。
4. 離線合約測試（C# 19 項 + PowerShell 623 項斷言，依延續紀錄）與本次新增的 `Persist_rejects_*`／`Constructor_rejects_a_missing_parent_owned_root_without_creating_it` 等測試共同覆蓋了「同一 baseline 分支下 ledger 保持未建立、無 CRM 呼叫」的斷言。

結論：本次 diff 只在既有 no-go 決策**之後**附加一個唯讀、去識別化的診斷輸出通道，未改變、未提前、也未延後任何會觸發 CRM 變更或 ledger 首次落盤的判斷點，`baseline-owner-unavailable` 分支的 no-mutation 保證維持不變。

---

### 總結
整體實作在 reparse 防禦、CRLF/UTF-8 邊界、跨程序 ledger 所有權轉移、單調 stage 驗證與 diagnostic 去識別化上設計嚴謹，且新增測試（PS 623 項合約斷言、C# ~19 個新 Fact）與程式碼變更範圍高度對應。本次未發現 Critical 問題；兩項 Warning 均為可延後處理的維護性/CI 覆蓋率議題，不影響是否可合併的判斷。未修改任何檔案，亦未建議放寬 CRM 寫入範圍、CE 版本、自動重試或 feature flag。

---
SESSION_ID: 8bf0189a-3f28-43a8-a224-818581e209ab
