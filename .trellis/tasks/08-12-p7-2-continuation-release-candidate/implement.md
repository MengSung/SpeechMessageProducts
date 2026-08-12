# P7.2 後續實作計畫

## 執行順序與品質閘門

### 1. 承接既有錯誤復原工作

- [x] 讀取 `harden-churchreport-error-recovery` 的 task／測試，將既有根因與本任務關係寫入研究紀錄，不建立第二個 service-lifecycle 修正實作。該任務已將後續的 operation-local service 修正交由本 continuation task 承接。
- [x] 建立 DownloadListManager service ownership 的失敗測試：兩個不同 service 依序呼叫時，共用 ToolUtility 的 service 欄位不得被任一呼叫覆寫。
- [x] 執行該測試，確認舊 forwarding 問題後以顯式 operation-scoped service 參數做最小修正，並保留原始 stack。
- [x] 執行 targeted isolation tests、Release build、encoding／CRLF 檢查；結果寫入本 task 與 CCG task。

#### 1a. DownloadIntegrateData request-local propagation（P7.4/P7.5 blocker）

- [x] 以紅燈／綠燈測試證明 operation-local `DownloadIntegrateData` 不保存／Dispose 借用 service，且 A/B marker、例外、輸出提交與 ToolUtility 欄位完全隔離。
- [x] 完成 group-leader 唯讀流程的 header/login、list/weekly、members/batch contact、chart、identity/follow-up helper 參數傳遞；個人回報與 legacy mutation path 不可 fallback，繼續 fail closed。
- [x] 對 session cached `ListManager` 過渡 overload 採 CRM I/O 前固定拒絕；完整 operation context 尚未接入產品前，不將此路徑列為 P7.4/P7.5 ready。
- [x] 執行隔離、例外、借用 service 未 Dispose、D–H local-only contract 與 targeted build；P7.4/P7.5 blocker 維持。

### 2. Slice C child diagnostics 與新 cycle gate

- [x] 建立 child no-go 的 regression，parent 僅接收 bounded 分類且不輸出 raw exception。
- [x] 修正 `live-evidence-incomplete` 保留為合法 no-go handoff，不被後續 xUnit assertion 覆寫為 child-process-failed。
- [x] 執行 PowerShell runner contract（277 checks）、fresh-fixture contract、P7.2 coverage validator 與對應 C# tests。
- [x] 執行一次新的 nonce／ledger／fresh-fixture Slice C cycle：preflight go、provision go、single ExecuteFixture no-go（`write-not-committed`）、strict exact cleanup go。依規則停止 CE 寫入家族，絕不重試。

### 3. Slice D–H 本機 capability

- [x] 逐 Slice 由 coverage matrix 與既有 call site 建立 operation contract；每份 contract 列出固定輸入／輸出、allowlist、baseline、partial-completion policy、rollback owner、cleanup 與 CE evidence gate。
- [x] 對 D donation lifecycle 建立 request validation、financial fixture contract、read-back／cleanup 的 unit／integration tests。
- [x] 對 E appointments 建立固定 create/update contract、owner isolation 與 cleanup tests。
- [x] 對 F onboarding 建立 multi-record ledger 與 reverse-cleanup tests。
- [x] 對 G fee lessons 建立 monetary/status reconcile、timeout no-replay 與 restore tests。
- [x] 對 H attendance 建立 weekly-report zero/exactly-one/duplicate/unavailable 分支、present-record upsert 與 cleanup tests。
- [x] 每一個 Slice 僅在其 C#／PowerShell tests 與 contract tests 全綠後標記「本機完成」；全部 CE 寫入維持禁止。

### 4. 候選版、檢查與交付

- [x] 建立 Release candidate manifest，逐 Slice 列出本機／CE／cleanup／rollout 狀態；CE 未證實者明示 fail closed。
- [x] 每個 Slice 邊界與候選版執行 targeted tests、Dynamics suite、Release build、編碼與行尾驗證、`git diff --check`、scope check。
- [x] 以 Gemini／Claude 45 秒上限執行分析；完整兩模型分析支持 session-state overload fail-closed。後續 reviewer 仍須依 check gate 執行。
- [ ] 更新 Trellis／CCG 紀錄；本任務完成時依 Trellis／CCG 歸檔與 scope-only commit 流程處理。
