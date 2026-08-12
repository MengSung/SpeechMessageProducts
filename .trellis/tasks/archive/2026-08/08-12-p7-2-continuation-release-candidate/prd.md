# P7.2 後續：Slice C 根因、D–H 本機實作與候選版

## 目標

在不重試已歸檔 P7.2 Slice C cycle、不改動共享或正式資料的條件下，完成一個可驗證的第一版 Release candidate。候選版必須保留 P7.2–P7.5 的安全閘門，並將「本機驗證」與「CE 9.1 實機證據」分開呈現。

## 範圍

- 軌道 A：先完成既有 `harden-churchreport-error-recovery` 的根因修正；釐清 child process 的 `live-evidence-incomplete` 分類、原始例外保留、CRM service ownership、逾時／Dispose／lease 的確定釋放。通過本機品質閘門後，才可執行一次新的 Slice C CE cycle。
- 軌道 B：依 coverage matrix 定義並實作 Slice D（donation lifecycle）、E（appointments）、F（contact onboarding）、G（fee lessons）、H（attendance）的本機 capability、契約、錯誤處理、rollback／cleanup 與隔離測試。
- 對每個 Slice 產出可審核的本機狀態、CE 狀態、未完成條件與 Release candidate 說明。

## 不在範圍內

- 不重新開啟、改寫或重跑 `.trellis/tasks/archive/2026-08/08-07-churchreport-write-action-function-migrations/` 的 historical/final cycle。
- 不修改週報、舊 fixture、共享資料、正式資料、CE 8.2、Official Worker、feature flag 或產品流量。
- 在缺少各 Slice 的 CE read-back、reconciliation 與 exact cleanup 證據時，不啟動 P7.4 切流或 P7.5 ToolUtility 移除。
- 不掃描或猜選 CRM Owner，且不讓 caller 指定 Owner、組織、端點、認證或任意 CRM 欄位。

## 功能與安全需求

1. 借用的 `IOrganizationService` 必須只存在於單一操作範圍，禁止寫回共享 `ToolUtility`、singleton、static、cache 或跨 request 狀態。
2. 重新擲回例外必須保留原始 stack trace；對外證據僅能輸出固定、去識別化分類，不得輸出 CRM ID、姓名、端點、帳密、token、cookie、原始 response 或原始 exception。
3. 每個有副作用的 capability 必須在 dispatch 前驗證固定 allowlist、task-owned fixture／ledger 與 baseline；遇到 timeout、ambiguous、no-go、read-back mismatch 或 cleanup uncertainty 時 fail closed 且不自動重試。
4. Slice C 的新 cycle 必須使用新 nonce、新 ledger、新 fresh fixture，並嚴格依序執行 bootstrap、read-only preflight、provision、一次 ExecuteFixture、read-back／reconcile、exact cleanup。
5. zero-active weekly report 時出席紀錄不得關聯週報；exactly-one-active 時必須精確關聯並 read-back；duplicate-active 或 unavailable 時不得寫入。
6. Slice D–H 在 Slice C 缺少完整 CE 證據時，只能進行本機設計、實作與測試；不得進行任何 CE 寫入。
7. 所有新／修改的 C# 檔案須為 UTF-8 無 BOM、CRLF、末尾 CRLF，具完整繁體中文 XML 文件，並有跨使用者隔離與資源生命週期說明。

## 驗收條件

- [ ] 既有 `harden-churchreport-error-recovery` 的根因、修正、TDD 證據與與本任務的承接關係均已紀錄；沒有為同一問題維護兩個獨立實作。
- [ ] DownloadListManager 與相關呼叫鏈不再把借用 CRM service 寫入共享 ToolUtility；回歸測試證明兩個獨立操作無 service／例外／資源交叉污染。
- [ ] child-process failure 會保留可追蹤的 bounded、去識別化診斷分類，且父程序不會把已知 no-go 誤報為成功或洩漏原始資料。
- [ ] Slice D–H 各自具本機 capability contract、allowlist、錯誤／timeout／部分完成／cleanup／隔離測試與可重現的本機驗證結果。
- [ ] Release candidate 明示每個 Slice 的本機與 CE 狀態；未取得 CE evidence 的能力維持 fail closed，沒有流量切換或 ToolUtility 移除。
- [ ] 新 Slice C CE cycle 僅在本機 gate 全綠後執行一次，並以 read-back、reconcile 與 exact cleanup 證明結果；若任一條件失敗，軌道 A 停止且留下去識別化紀錄。
- [ ] 每個 Slice 邊界與候選版交付前通過相稱 tests、Release build、encoding／CRLF、`git diff --check`、scope check 與可用的雙模型／降級審查紀錄。
