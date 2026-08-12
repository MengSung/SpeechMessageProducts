# Slice C child diagnostics：根因與本機契約驗證

## historical `live-evidence-incomplete` 的實際分類路徑

歷史 `ExecuteFixture` child 會先寫入嚴格、去識別化的 Slice C evidence，之後以 xUnit 的
`outcome.Should().Be("go")` 檢查整體操作。當受控操作已完成 child-owned store、runtime 與
logger cleanup，但任一 operation 未能形成完整 live evidence 時，evidence 的固定結果本來是
`no-go / live-evidence-incomplete`；然而最後 assertion 仍將 child 改為非零結束。

parent 在 drain stdout/stderr 並觀察到非零 exit 後，必須 fail closed：它不能信任 child 的完整
operation projection、read-back 或 cleanup authority，只能回傳 `no-go / child-process-failed`，並在
strict parser 成功時投影單一 `diagnosticCategory=live-evidence-incomplete`。因此 historical
`child-process-failed / live-evidence-incomplete` 是 child assertion 改變 lifecycle exit 的結果，
不是 parent 遺失 CRM exception 或重試條件。

## 修正後的信任與資源邊界

- child evidence 仍只可寫入 parent-owned、non-reparse temporary root 中的固定檔名，且 schema、
  UTF-8 no-BOM、CRLF、大小、operation ID 與 reason 必須全部通過 strict parser。
- child 已完整寫出合法 no-go evidence 且 store、runtime、logger cleanup 成功時，以 zero exit
  表示 process lifecycle 完整；parent 解析後保留原始 bounded reason，例如
  `live-evidence-incomplete`，並以自己的 nonzero handoff 宣告 CE no-go 與禁用重試。
- child 的非零 exit 仍固定為 `child-process-failed`；最多只可投影既有 allowlist 的單一診斷分類，
  不採用 operations、operationExecuted、read-back、cleanup 或 retry authority。
- child evidence 若夾帶 raw exception、local path、CRM identifier、credential 或任何額外欄位，
  parent 在輸出前拒絕整份 evidence，僅回傳 `evidence-result-unavailable`。這些資料不會進入
  console JSON、TRX、session、cache 或下一個 child process。
- child stdout/stderr 會由 parent drain，以釋放 pipe 與 process resource，但不會被 relay 至
  operator JSON。若 stream 行為使 child 結束狀態不可信，parent 仍回傳
  `child-process-failed`，且最多只保留 strict evidence 的單一 bounded diagnostic category；
  raw stream 內容不會跨越 parent boundary。
- cleanup failure 仍高於一般 no-go；child 無法確認唯一 owner 已釋放時，最終分類必須是
  `cleanup-failure`，不得宣布完整 evidence。

## TDD 與本機結果

- Red：新增 `Execute_evidence_finalizer_preserves_complete_live_evidence_incomplete_reason` 時，
  因尚未有 execute finalizer 而出現 `CS0103`。
- Green：加入僅處理 bounded scalar 的 execute finalizer，並讓 live child evidence 組裝使用它；
  cleanup 成功保留原 reason，cleanup 失敗固定覆寫為 `cleanup-failure`。
- PowerShell synthetic child contract：zero-exit strict no-go 保留
  `live-evidence-incomplete`；故障注入的 raw exception/path/CRM identifier 額外欄位被 strict
  parser 拒絕，parent JSON 不含三個 sentinel。另一個 stdout/stderr sentinel 注入確認 parent
  drain 後不 relay stream 資料；結果維持 fail-closed `child-process-failed` 加單一 bounded category。

本紀錄只描述離線 contract 與 historical evidence 分類。沒有啟動 CE child、沒有建立 fixture 或
ledger、沒有變更 weekly report、feature flag、流量、CE 8.2 或 Official Worker；Slice C 仍未取得
可發布的 CE live evidence。
