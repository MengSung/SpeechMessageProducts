# P7.4 點名名單成員唯讀資料平面設計

## 決策

採取「獨立 local-only data plane」方案。它只建立安全、固定且有界的 lookup contract，不改動三條
混合 legacy consumer。這讓日後具備 principal-derived authorization scope 的 consumer 能使用同一個 capability，
而不讓目前任何 session／Entity／write flow 成為 Gateway authority。

## 資料流與信任邊界

```text
未來已授權的 server request（不在本 child）
  -> AppNamedMembershipReadRequest(contactId, deployment profile, workload)
  -> IAppNamedMembershipReadClient
  -> IDynamicsOperationExecutor
  -> Data8ProfileOperationExecutor
  -> Package01Data8ReadOperations
  -> fixed QueryExpression(list + listmember)
  -> AppNamedMembershipRecords wire branch
  -> request-local readonly DTO snapshots
```

`contactId` 是上游已授權 server code 才能提供的 locator，絕非 authorization、profile、connector 或 credential
authority。本 child 沒有 consumer 或 HTTP endpoint，因此不會把 browser data 接入 executor。未來 consumer 必須先
建立 immutable authorization scope 並驗證 target contact，才能呼叫本 contract。

## 固定契約

- Operation：`list.membership.retrieve.appnamed.by.contact`。
- Template：`list.membership.appnamed.by.contact.v1`。
- Input：non-empty contact GUID、non-empty deployment-owned profile alias、non-empty server workload subject。
- Query：固定 `listid`、`listname` projection；固定 `new_app_named=true`、`statecode=0`；固定
  `listmember.entityid=contactId` relationship；依 `listname` ordinal ascending、`listid` ascending 排序。
- Bounds：最多 32 列、單一 page、32 KiB scalar payload；`MoreRecords`、null row、empty/duplicate list ID、
  identity/type mismatch 或 text-byte overflow 一律 fail closed。
- Response：每列僅 `ListId` 與 nullable `ListName`。不輸出 Entity、EntityCollection、lookup graph、formatted
  value、cookie、query、endpoint、credential、profile 或 raw exception。

32 是「單一 contact 的 currently active app-named memberships」硬上限；若資料異常超出，Gateway 拒絕結果而非
悄悄截斷或把不完整資料交給 write-adjacent caller。

## 隔離、資源與回滾

client 是 stateless singleton，只保存 executor/logger。每次 request 新建 parameter map、wire mapping list 和
`ReadOnlyCollection`；不保存 DTO、contact、profile、workload、session、cache、timer、background task 或
cancellation registration。executor/pool 保有 lease、permit、fault eviction、transport timeout/cancellation 和
dispose 的單一 ownership。取消、fault、partial query 或 MoreRecords 均不 publish response。

rollback 只需 revert 本 child 的 registry/connector/client/test code；因沒有 consumer/gate/CE write，沒有 runtime
data cleanup。legacy callers 保持不變，故本 child 不得宣稱 customer traffic、CE、P7.5 或 P8 evidence。

## 測試策略

TDD 依序新增 registry contract、Data8 fixed-query／strict bound tests、ProductClient input/response/isolation tests
與 DI test。測試 fake executor/service 不接觸 CE。A/B test 交錯兩個 profile/workload/contact 的 response，確認各自
得到重新配置且不可變的 DTO collection；取消直接傳遞，錯誤資料不得進行第二條 fallback I/O。
