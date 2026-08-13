# P7.1 app-named 小組名單目錄強型別讀取

## 目標

為 authoritative matrix 的 `ORG-CALL-00065`／`list.catalog.retrieve.appnamed.smallgroups` 建立一項獨立、零 caller parameter、固定查詢、bounded、DTO-only 的 Data8／ProductClient 唯讀能力。它只提供本機 contract evidence，絕不遷移 ChurchReport consumer、啟用 feature gate、發出 CE request、改動 fixture／週報／流量，或開始 P7.5／P8。

## 已確認基準

- `ORG-CALL-00014` 已封存；它是不同 ID、template、response branch，絕不能重用為本 operation。
- legacy `IListService.RetrieveSmallGroupLists()` 固定讀取 list 的 name、code、last-used、purpose、兩個 leader lookup 與 ID，固定 active／purpose／app-named filter，並排除既有退出名稱 pattern。
- 既有 ChurchReport `DownloadListManager`、`ListManagementDataManager` 與 `InMemoryDataContextSmallGroup` 含 request/session 不可證明安全的共享可變狀態、ToolUtility 和 SDK graph；本 child 不得 reference 或修改它們。
- P7.2 Slice C historical cycle 已 cleanup 且永久 closed；P7.5 prerequisite 仍為 no-go；這些狀態不因本機 read capability 而改變。

## 需求

1. registry 必須定義獨立 server-owned operation ID、template ID、零參數、有限列數／頁數／byte budget 與 closed response branch；operation 不接受 caller-selected filter、list ID、leader、profile、connector、endpoint 或 credential。
2. Data8 connector 必須只使用有界 `RetrieveMultiple` 的固定 `QueryExpression`，在 connector lease scope 內立即把 CRM Entity／lookup graph 投影成 immutable scalar wire record。leader 欄位只能是 nullable GUID；不得保留名稱或 SDK type。
3. ProductClient 必須在 outbound I/O 前拒絕空 profile/workload，在 response 收到後精確檢查 operation、kind、branch、non-null row 與 non-empty list ID；傳回新的 readonly DTO snapshots，且不留 cache、retry、fallback、timer、background work 或跨 request mutable state。
4. 所有連線、lease、permit、transport fault eviction、timeout/cancellation cleanup 仍歸 executor/pool 的既有單一 owner；本 capability 不可攔截或重用 ambiguous connection state。
5. matrix 只能更新此 row 的 registry／Data8 executor／ProductClient local implementation。consumer、CE 8.2/9.1、Embedded/Dedicated、rollout/rollback owner 與 temporary-legacy 必須保留真實 pending 狀態。

## 驗收條件

- [ ] 新 operation、template、closed response branch、wire record 及 DTO 明確不同於 `ORG-CALL-00014`，且只含 allowlisted scalar。
- [ ] 固定 query、projection、sorting、paging／byte bounds、invalid parameter zero-I/O、fault/cancel、malformed response、source mutation、A/B profile/workload interleaving 均有 targeted tests。
- [ ] ChurchReport、ToolUtility、shared cache、feature settings、CE/fixture/traffic、P7.5/P8 沒有修改；consumer 不會意外接上新 client。
- [ ] 完成 Trellis／CCG artifacts、雙模型 bounded analysis/review、targeted tests、Dynamics/solution Release tests、Release build、matrix validator、encoding/CRLF、`git diff --check`、scope scan、scope-only commit 與 archive。
