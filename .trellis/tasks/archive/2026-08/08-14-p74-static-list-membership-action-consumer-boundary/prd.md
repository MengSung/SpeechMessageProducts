# P7.4 靜態名單成員動作消費端邊界

## 目標與使用者價值

評估權威 matrix 的 `ORG-CALL-00011`（`list.members.add.many`）與
`ORG-CALL-00012`（`list.members.remove.one`）是否能從 ChurchReport 的既有
`ListManagementDataManager` 安全遷移至已實作的 typed ProductClient。此 child 的成功定義
不是強行接線，而是以可重複的程式碼與契約證據決定：只有在不混合 legacy CRM 寫入、可確定
授權、可定義冪等與完整補償時，才允許進入 disabled-by-default consumer 實作。

## 已確認事實

1. immutable matrix 顯示兩個 capability 的 registry、Data8 executor 與 ProductClient 都已實作，但
   consumer 為 `not-migrated`，CE 8.2／9.1 與 host evidence 均非完成狀態。
2. `ListManagementDataManager` 現在將 remove/add member action 和 contact primary-list lookup 更新、
   出席紀錄處理、legacy `Entity` retrieve/update 放在同一個業務流程中。
3. 對 list member action 單獨開啟 typed path 會造成同一使用者請求同時包含 Gateway mutation 與
   ToolUtility mutation；它既不是單一路徑，也沒有可證明的 composite read-back／rollback。
4. P7.2 舊 Slice C 是 `write-not-committed` no-go，且已 cleanup；不得重試、復用或修改任何舊
   fixture、nonce、ledger 或 descriptor。
5. P7.4 capacity/non-overlap evidence 尚未成立；所有 deployment-owned feature gate 必須保持 false，
   不得執行 CE、切流、P7.5 或 P8。

## 需求

1. 將本 child 限定為 repository-only 的消費端可行性評估；不得新增或修改 runtime CRM 行為。
2. 將發現持久化為明確 no-go：現有流程不能安全地只替換 add/remove member action。
3. 清楚列出恢復條件：未來 child 必須先把整個 composite 拆成 typed DTO-only operation family，包含
   server authorization、固定欄位/relationship allowlist、同一 deadline、read-back、reconcile、
   reverse-order cleanup 與 single rollback owner，才能再評估 consumer migration。
4. 不得以 SDK `Entity`／`EntityCollection` bridge、request-time fallback、dual-write、猜選 Owner、
   靜態 session/cache 或重送 unknown write outcome 製造進度。
5. 結案後選擇另一個獨立、已有 foundation 且可安全隔離的 P7.4 capability；本 child 不得阻斷該後續工作。

## 驗收條件

- [ ] task artifacts 正確記錄 add/remove action 與 legacy composite 的程式碼證據，以及不遷移的理由。
- [ ] 設計明定 fail-closed、禁止 dual-write、保持 feature gates=false，且不宣稱 CE、cutover、P7.5 或 P8。
- [ ] 本機檢查證明此 child 沒有 runtime source/configuration/CE mutation；僅新增 task/CCG 記錄。
- [ ] CCG 限時分析結果或明確的降級原因已寫入紀錄。
- [ ] child 依 Trellis/CCG 完成 check、scope-only commit 與 archive，父 task 保有下一個可執行項目。

## 不在範圍

- 改動 ChurchReport runtime、ToolUtility、SDK entity workflow、feature gate、CE fixture、CE mutation、
  流量切換、P7.5 removal、P8 或正式資料。
