# P7 尚餘能力重新基準化實作計畫

## Phase 1：分析與規劃

- [x] 讀取使用者目標、AGENTS.md、Trellis workflow、backend isolation／Gateway specs、P7.0／P7.1／P7.2 封存 evidence。
- [x] 以 45 秒上限執行 Gemini／Claude architecture analysis；兩者完成，artifact 已保存。
- [x] 更新 parent `prd.md`、`design.md`、`implement.md`、`roadmap-p5-p7.md` 與 `task.json` 的 P7 現況與 next action。
- [x] 建立新 CCG task，記錄 L+／high-risk、analysis、dual-model artifact 與本 child 檔案範圍。

## Phase 2：建立 authoritative matrix

1. [x] 撰寫 analyzer／validator red tests：來源必須恰有 70 rows、不得接受重複 call site、不得把 declaration/local-only/disabled gate 當成 executor/consumer/CE evidence，且輸出欄位必須有界。
2. [x] 執行 RED 指令，確認失敗原因是 analyzer／schema 尚不存在，而非測試錯字或既有 build 問題。
3. [x] 建立 task-owned rebaseline analyzer，僅讀取 allowlisted source／archive，產生 `authoritative-gap-matrix.json`。
4. [x] 執行 GREEN targeted tests，加入 deterministic ordering、canonical phase0 checksum、CRLF、P7.1/P7.2 known-evidence、Package02 multiline constant detection 與 ChurchReport legacy dependency guards。
5. [x] 以 matrix 寫出 `rebaseline-summary.md`：剩餘 P7.1/P7.2 family、P7.3、P7.4、P7.5 及 P8 prerequisite。

## Phase 3：檢查與交付本 child

- [x] 執行 matrix validator、10 項 unit tests、Python compile／JSON parser／canonical checksum、33 項 targeted Dynamics tests、完整 solution tests、Release build、byte-level UTF-8 無 BOM／CRLF／final CRLF、`git diff --check`、scope check。
- [x] 執行 Gemini／Claude review（各最多 45 秒）；Gemini Warning 已修正，Claude 逾時，已紀錄降級且不重試。
- [x] 更新 Trellis／CCG artifacts、reusable Gateway spec、task metadata 與 parent roadmap；後續只允許本 child 與 required parent planning update 的 scope-only commit/archive。

## 後續執行順序（本 child 完成後）

1. 只針對 matrix 判定尚餘、可獨立驗收的 P7.1/P7.2 capability family 建立下一 child；所有 CE operation 維持單次 fresh cycle、精確 read-back、reconcile、cleanup、no-retry。
2. 建立 P7.3 `churchreport-special-resource-migrations`，先完成 payload/page/queue/resource owner hard limits 與 lifecycle tests。
3. 建立 P7.4 `churchreport-productclient-cutover`；逐 capability、disabled-by-default feature gate、drain-first/non-overlap 及 rollback owner，不做 request-time fallback。
4. 只有 matrix 無 temporary legacy／unclassified rows、ChurchReport zero-reference scan 全綠、完整 parity/soak/drain/rollback evidence 具備時，建立 P7.5 `churchreport-toolutility-removal`。
5. P7.5 commit/archive 與 immutable handoff 完成後才建立 P8 parent 與 P8.0–P8.4。P8 外部前置條件缺失時只完成 repository-side package／validator／runbook／sanitized handoff，絕不猜測 deployment identity、DNS、TLS 或 secret。
