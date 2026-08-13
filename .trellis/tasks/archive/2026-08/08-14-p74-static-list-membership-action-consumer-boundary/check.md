# P7.4 靜態名單成員動作消費端邊界 Check 紀錄

## 結論

`ORG-CALL-00011` / `ORG-CALL-00012` 的 consumer migration 是 **no-go**。這不是 Data8 或
ProductClient 缺少 action contract；原因是 ChurchReport 既有 `ListManagementDataManager` 將 membership
add/remove 和 contact primary-list、出席紀錄、legacy `Entity` retrieve/update 寫入交織成同一 composite。

若只替換 member action，會使一個 request 混用 Gateway 寫入與 ToolUtility 寫入，缺少共同 authorization
boundary、transaction、read-back/reconciliation、reverse-order cleanup 與單一 rollback owner。這會造成
partial completion / split-brain 風險，故維持 `temporary-legacy`、不接線、不開 gate、不操作 CE。

## 證據與範圍

- 權威 matrix：`ORG-CALL-00011` / `00012` 均為 registry/executor/ProductClient implemented，但 consumer
  `not-migrated`、CE/host evidence pending。
- runtime source evidence：`ListManagementDataManager.cs` 的 membership action 緊接 contact list lookup 更新、
  `Entity` retrieve/update 與 attendance workflow。
- 本 child 僅新增 Trellis/CCG task artifacts；沒有改動 ChurchReport runtime、settings、feature flag、
  ToolUtility、CRM SDK、CE fixture 或外部資料。
- P7.2 舊 Slice C `write-not-committed` / cleanup-complete 保持封存，沒有讀寫、重試或復用其中資產。

## 審查

由 CCG self-healing runner 執行 architecture review：Gemini 完成，結論 PASS、Critical 0、Warning 0，支持
no-go decision。Claude 因 provider session limit 無可用輸出。此結果為「雙模型未完成／single-model degraded
fallback」，不得表述為完整雙模型審查。

final reviewer 亦依 45 秒上限啟動，但在 deadline 前只完成 prompt/health artifact，沒有任何 backend findings；
因此記錄為「雙模型未完成」，改以本機 scope、source trace、encoding/CRLF 與 `git diff --check` 驗證，沒有把
未完成的 reviewer 啟動宣稱為審查通過。

## 恢復條件

只能由新的 P7.2 child 規劃完整 list-transfer/attendance/contact composite。它必須有 server authorization、
fixed DTO/allowlist、單一 deadline、idempotency、exact read-back/reconciliation、僅對 fresh task-owned fixture
執行 reverse-order cleanup、single rollback owner 及對應 CE/host/parity evidence。任何 timeout、ambiguous、
no-go、read-back mismatch 或 cleanup uncertainty 都停止該 family，絕不重試。
