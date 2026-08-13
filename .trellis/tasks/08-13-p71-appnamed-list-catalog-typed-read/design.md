# P7.1 App-named 名單目錄強型別讀取能力設計

## 架構決策

`ORG-CALL-00014` 是一個沒有 browser locator 或 consumer-provided selector 的固定 list catalog read。它應
先作為 P7.1 data capability 完成，而不是修改現有 ChurchReport `EntityCollection` route。邊界如下：

```text
future server-authorized P7.4 consumer (不在本 child)
  -> IAppNamedListCatalogReadClient
  -> IDynamicsOperationExecutor
  -> Data8ProfileOperationExecutor / Gateway executor
  -> Package01Data8ReadOperations
  -> limited fixed QueryExpression RetrieveMultiple
  -> OperationResponseData.AppNamedListCatalogRecords
  -> request-local AppNamedListCatalogRecordDto snapshots
```

operation、entity、column set、filters、sort、paging、response kind 和 bounds 都由 registry / connector
固定。profile/workload 由 deployment composition 決定；它們不是 browser caller 能選擇的 authority。未來
consumer 還必須自行證明 authorization 與 cache policy，因為「catalog」並不等同於可在所有 user/profile/
tenant 間共用的 cache data。

## 封閉資料契約

新增 `OperationIds.ListCatalogRetrieveAppNamed`、`OperationResponseKind.AppNamedListCatalogRecords`、
`AppNamedListCatalogRecord` 和 `OperationResponseData.AppNamedListCatalogRecords`：

| 欄位 | 來源 | 規則 |
| --- | --- | --- |
| `ListId` | `listid` | 必填且非空，否則拒絕整個 response。 |
| `ListName` | `listname` | nullable pure scalar；UTF-8 budget 計量。 |
| `CreatedFromCodeOption` | `createdfromcode` | nullable OptionSet numeric value；不傳 formatted metadata。 |
| `LastUsedOn` | `lastusedon` | nullable UTC `DateTimeOffset`；不保留 CRM timezone graph。 |
| `Purpose` | `purpose` | nullable pure scalar；UTF-8 budget 計量。 |

response union constructor/factory 必須 materialize source collection。ProductClient 再 materialize 一份 DTO
collection，確保 envelope 或 fake source 在回應建立後變動時，已發佈結果不變。每一筆都使用新的 DTO；
client 是 stateless singleton，只保存 DI owner 的 executor/logger 參考。

## Data8 query、bounds 與資源生命週期

connector 的 private factory 固定：

```text
entity: list
ColumnSet: listid, listname, createdfromcode, lastusedon, purpose
filters: statuscode = 0, purpose = 小組名單, new_app_named = true
order: listname descending, listid ascending
PageInfo: count=128，最多 registry 的 4 頁
```

每頁由 `RetrieveMultiple` 取得後立即驗證、投影並累積 size budget。任何 MoreRecords 但缺 paging cookie、
超過 page/item/byte limit、null page、ID/entity/type mismatch 都丟出；不要回傳 partial result。Data8 lease、
permit、connector 與 transport 仍完全由現有 executor/pool owner 處理；capability 不新增 cache、retry、timer、
background work、stream 或 other resource owner。timeout/cancel/fault 都不重試，也不讓不確定 connector reuse。

## 隔離與錯誤矩陣

| 條件 | 行為 |
| --- | --- |
| request parameter map 非空、operation 不在 allowlist | 在 router/pool 前拒絕。 |
| profile/workload 空白 | ProductClient outbound I/O 前拒絕。 |
| Data8 page null、entity/ID/type 無效、page/bytes 超限 | fault path；不公開 rows。 |
| response operation/discriminator/branch 不符 | ProductClient fail closed，沒有 DTO 發佈。 |
| envelope 後來源 collection 變動 | envelope + ProductClient 兩層 copy，結果不受影響。 |
| A/B profile/workload 交錯完成 | 每個 call 只建立 request-local parameters/wire/DTO；無 cache/last-result。 |
| cancellation/timeout | 原 token 向下傳遞；不 retry、不產生 partial list。 |

## 與 ORG-CALL-00065 的隔離

`ORG-CALL-00065` 有不同 template、額外 name exclusion 和 leader fields，且其 consumer 現存 shared
`EntityCollection` cache。為確保沒有錯誤的 cache/status/field/consumer parity claim，本 child 不為它加
registry entry，不修改 cache，也不建立 generic list query。日後須以另一個 child 分別設計 server authorization、
DTO projection、cache boundary、rollout and rollback。

## Rollback 與證據範圍

rollback 是「不接 consumer，保持沒有 gate」。刪除/停用本能力不改動 existing ToolUtility code path，也不能
用 request-time fallback 或 dual-read shadow 取代 rollback。本 child 結案後 matrix 最多記載 registry、executor、
ProductClient local completion；consumer、CE 8.2/9.1、Embedded/Dedicated host、traffic/P7.5/P8 evidence 必須
原樣保留 pending/not-migrated。
