# P7.1 app-named 小組名單目錄強型別讀取設計

## 邊界與資料流

此 child 是純資料能力，與 ChurchReport legacy consumer 明確分離：

```text
未來受權的 P7.4 consumer（不在本 child）
  -> IAppNamedSmallGroupListCatalogReadClient
  -> IDynamicsOperationExecutor
  -> Data8ProfileOperationExecutor
  -> Package01Data8ReadOperations
  -> 固定且有界的 QueryExpression RetrieveMultiple
  -> SmallGroupAppNamedListCatalogRecords wire branch
  -> request-local SmallGroupAppNamedListCatalogRecordDto snapshots
```

operation、template、entity、attributes、filter、sort、paging、response kind 與 byte bounds 均由 compiled registry 決定。profile alias 和 workload subject 僅可由 deployment／server composition 提供；consumer selector、list ID、leader、FetchXML、cache key、endpoint、credential 與 CE version 不屬於此 interface。

## 固定資料契約

Operation：`list.catalog.retrieve.appnamed.smallgroups`。Template：`list.catalog.appnamed.smallgroups.v1`。Query 對 `list` 固定投影 `listid`、`listname`、`createdfromcode`、`lastusedon`、`purpose`、`new_contact_race_leager_list`、`new_contact_family_leader_list`；固定 `statuscode=0`、purpose、app-named 和 legacy 退出名稱排除條件，按 `listname` descending、`listid` ascending。每頁、累積頁數、row count 和 UTF-8 scalar bytes 皆由 registry 的 finite bound 限制；null page、MoreRecords 無 cookie、identity/type/lookup 不符或超限均 fail closed。

Wire record 和 DTO 只包含 list ID、nullable name/code/UTC timestamp/purpose、nullable race-leader contact ID 與 nullable family-leader contact ID。Data8 只能把 `EntityReference.Id` 拷貝為 GUID；任何 `EntityReference.Name`、Entity、EntityCollection、formatted values、paging cookie、raw exception、endpoint、credential、profile 或 connection state 都不得離開 connector scope。

## 隔離、生命週期與 rollback

ProductClient 是無狀態 singleton；每次執行配置新的零參數 read-only map 和新的 backing list/readonly collection。它不快取 DTO、wire response、profile、workload 或 failure，不建立 retry/timer/background task，不保存 cancellation registration。executor 仍擁有 Data8 lease、permit、fault eviction、transport 和 cancellation cleanup；timeout、cancel 或 fault 不會將 connection 或 partial payload 交給下一個 request。

此 child 的 rollback 是 scope-only revert registry/connector/client/test code，因為無 consumer/gate/CE write。legacy `RetrieveSmallGroupLists()`、`DownloadListManager`、`ListManagementDataManager`、`InMemoryDataContextSmallGroup` 及其 shared cache 完全不變，故不得宣稱 consumer/traffic rollback 已完成。未來 consumer cutover 必須另有 server authorization、full cache partition、bounded retention、invalidation/drain、A/B isolation、capacity admission、CE/parity/rollback evidence，不能用本 child 的 local read 取代。

## 測試策略

TDD tests 先證明新 operation 尚不存在，再逐層證明 registry response union、fixed query/projection/bounds、executor validation、ProductClient mapping/cancellation/zero-I/O/A-B isolation 與 DI lifetime。另加 source scope test 確保新 capability 不 reference ChurchReport/ToolUtility/cache/SDK Entity bridge。所有測試為 fake executor/service，不發出 CE。
