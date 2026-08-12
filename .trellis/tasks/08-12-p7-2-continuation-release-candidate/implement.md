# P7.2 後續實作計畫

## 執行順序與品質閘門

### 1. 承接既有錯誤復原工作

- [ ] 讀取 `harden-churchreport-error-recovery` 的 task／測試，將既有根因與本任務關係寫入研究紀錄，不建立第二個 service-lifecycle 修正實作。
- [ ] 建立 DownloadListManager service ownership 的失敗測試：兩個不同 service 依序呼叫時，共用 ToolUtility 的 service 欄位不得被任一呼叫覆寫。
- [ ] 執行該測試，預期在目前實作失敗並證實 cross-operation 寫回。
- [ ] 以顯式 operation-scoped service 參數做最小修正，將所有 `throw e;` 改為 `throw;`，補充繁體中文 XML／生命週期文件。
- [ ] 執行 targeted controller／manager tests、Release build、encoding／CRLF 檢查；將結果寫入 task 與既有 CCG task。

### 2. Slice C child diagnostics 與新 cycle gate

- [ ] 先建立 child no-go 的失敗測試，預期 parent 只能收到 bounded 的 `operation`、`runtime`、`evidence`、`cleanup` 類別，沒有 raw exception。
- [ ] 確認紅燈後，實作固定診斷分類與 parent mapping，確保已知 `live-evidence-incomplete` 不會被泛化為不可判讀的成功。
- [ ] 執行 PowerShell runner contract、fresh-fixture contract、P7.2 coverage validator 與對應 C# tests。
- [ ] 僅在所有本機 gates 通過後，啟動一次新的 nonce／ledger／fresh-fixture Slice C cycle；按 bootstrap、preflight、provision、single execute、read-back/reconcile、cleanup 串行記錄。

### 3. Slice D–H 本機 capability

- [ ] 逐 Slice 由 coverage matrix 與既有 call site 建立 operation contract；每份 contract 列出固定輸入／輸出、allowlist、baseline、partial-completion policy、rollback owner、cleanup 與 CE evidence gate。
- [ ] 對 D donation lifecycle 建立 request validation、financial fixture contract、read-back／cleanup 的 unit／integration tests。
- [ ] 對 E appointments 建立固定 create/update contract、owner isolation 與 cleanup tests。
- [ ] 對 F onboarding 建立 multi-record ledger 與 reverse-cleanup tests。
- [ ] 對 G fee lessons 建立 monetary/status reconcile、timeout no-replay 與 restore tests。
- [ ] 對 H attendance 建立 weekly-report zero/exactly-one/duplicate/unavailable 分支、present-record upsert 與 cleanup tests。
- [ ] 每一個 Slice 僅在其 C#／PowerShell tests 與 contract tests 全綠後標記「本機完成」；全部 CE 寫入維持禁止。

### 4. 候選版、檢查與交付

- [ ] 建立 Release candidate manifest，逐 Slice 列出本機／CE／cleanup／rollout 狀態；CE 未證實者明示 fail closed。
- [ ] 每個 Slice 邊界與候選版執行 targeted tests；候選版執行 solution tests、Release build、編碼與行尾驗證、`git diff --check`、scope check。
- [ ] 以 Gemini／Claude 45 秒上限執行分析與審查。逾時或額度限制，紀錄「雙模型未完成」並以本機驗證繼續。
- [ ] 更新 Trellis／CCG 紀錄；本任務完成時依 Trellis／CCG 歸檔與 scope-only commit 流程處理。
