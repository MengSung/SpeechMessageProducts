# P7.2 週日出席與週報寫入能力家族

規劃決策、來源稽核結論與 CCG 降級審查狀態見同目錄的 `planning-decision.md`。本 task 可進入
本機 TDD，但尚未具備 CE preflight、fixture、CE write、consumer cutover、feature gate、traffic、
P7.5 或 P8 的啟動條件。

## 目標

將 `ORG-CALL-00063` 所揭露的「聚會統計查詢、出席紀錄建立或更新、聚會統計關聯與週報重算」
拆成可驗證、可回復且不重播的 P7.2 寫入 capability family。第一輪只建立治理與本機 contract，
不改動既有 ChurchReport consumer、ToolUtility、feature gate、CE 資料或流量。

## 已確認事實

- 歷史 P7.2 Slice C 是 `write-not-committed` no-go，且 exact cleanup 已完成。舊 nonce、ledger、
  fixture、descriptor 與 evidence 永久不可復用或重試。
- `ORG-CALL-00063` 的 Package03 typed read 已存在：它是 CE 9.1、固定 UTC Sunday、bounded page／byte／
  row、無 paging cookie／CRM Entity 的 read-only projection；這不是出席寫入授權。
- `PersonalQrCodeUtility` 與 `SundayQrCodeUtility` 會在同一流程讀取完整 meeting-statistics／present-record
  Entity，接著 Create／Update 出席紀錄、寫入 meeting relationship、觸發週報重算，並含靜態 lock。
  因此它不可當作 P7.4 DTO-only read consumer 直接切換。
- 唯讀 source audit 已確認沒有 ChurchReport production caller 使用現有
  `IPackage03SpecialResourceClient.RetrieveMeetingStatisticsAsync`；現有 caller 仍由
  `ToolUtility`／SDK Entity 驅動寫入相鄰流程。
- P7.5 prerequisite report 仍為 `no-go`；所有 gate 維持 false，P8 不得建立或啟動。

## 功能與安全需求

1. 先完成完整 source audit，精確分類既有 QR signing 所有 read、Create、Update、relationship、weekly-report
   aggregate、通知與 idempotency 行為；不得以 partial read path 混接 legacy write。
2. 第一個可實作 operation 必須固定 server-owned operation ID、bounded request／response DTO、server-derived
   authorization scope、idempotency key、pre-write ledger owner、exact read-back／reconcile、rollback owner、
   deterministic cleanup、timeout/no-replay policy 與 A/B isolation test。
3. 每一個 mutation（present-record create、present-record update、meeting relationship、weekly-report recalculation）
   均須為獨立 operation 或在已證實的 atomic boundary 內；不得由 generic CRUD、caller-selected entity／field、
   QR dynamic attribute、caller-supplied owner、profile、credential 或 endpoint 直接驅動。
4. CE live evidence 僅能於新 task-owned fixture、new nonce、new ledger、read-only preflight=go 後，以一次
   controlled dispatch 執行。timeout、ambiguous、no-go、read-back mismatch 或 cleanup uncertainty 立即停止該
   mutation family 且不重試；不相依的本機 work 可繼續。
5. 所有 `.cs`／`.cshtml` 變更遵守 AGENTS.md：完整繁體中文文件、UTF-8 無 BOM、CRLF、final CRLF，且不得有
   session、cross-user、cross-profile、memory 或 resource leakage。

## 驗收標準

- [x] source audit 清楚列出既有 QR signing 的 mutation graph、resource owner、authorization 缺口與獨立
      operation 邊界。
- [x] PRD、design、implement 定義 first safe slice，且不把 Package03 read contract 誤稱為 consumer/CE evidence。
- [ ] 實作前具有 fail-first local tests，實作後具 targeted tests、A/B isolation、timeout／cleanup assertions。
- [ ] 各 mutation family 具 exact read-back、reconciliation、rollback、cleanup 與 no-replay 設計。
- [ ] 在所有 quality gates 通過前，不變更 feature gate、既有 QR consumer、ToolUtility reference、CE／traffic、
      P7.5 removal 或 P8。

## 不在範圍內

- 重播歷史 Slice C、修改 historical task 或復用其 fixture/ledger/descriptor。
- 一次切換 PersonalQrCodeUtility、SundayQrCodeUtility 或全部 QR workflow。
- CE 8.2、Official Worker、正式流量、P7.5 ToolUtility removal 或 P8 deployment。
