# P7.3 ChurchReport 特殊資源能力遷移：技術設計

## 邊界與資料流

每項能力遵守固定資料流：

```text
typed ProductClient request
  -> IDynamicsOperationExecutor
  -> Data8ProfileOperationExecutor（前置驗證、profile/generation 路由）
  -> generation-owned connector lease
  -> server-owned Data8 request/query/update/read-back
  -> OperationResponseData closed union
  -> typed ProductClient result
```

產品只看見純值 DTO。`IOrganizationService`、`Entity`、`RetrieveAttributeRequest`、
`QueryExpression`、FetchXML、paging cookie、raw stream、decoder、endpoint、credential、token 與
transport exception 僅可存在 connector request scope。lease 仍由既有 executor 的 `await using`
唯一擁有；任何 projection、page、read-back 或 response branch 不符都會標記 faulted。

## 五個封閉 capability

| Operation | Request | Response | 固定資料邊界 |
| --- | --- | --- | --- |
| image retrieve | `contactId` | copied image bytes + closed media kind | 固定 contact `entityimage`，不回傳 stream 或 CRM entity |
| member image update | `contactId` + copied image payload + idempotency key | changed/read-back-confirmed | 固定 contact `entityimage`，讀回必須逐 byte 相符 |
| new-person image update | 同上 | changed/read-back-confirmed | 與前列可共用 connector primitive，但保留獨立 operation/policy |
| option-set metadata | closed metadata target | ordered option DTOs | server allowlist、無 raw metadata/cache key |
| meeting statistics | UTC Sunday | bounded meeting DTO list | 固定 meeting entity/filter/projection/order，無 cookie/FetchXML |

影像 payload 使用封閉型別與多次 defensive copy。固定上限設在 Gateway 64 KiB wire cap 以下；
解碼時再驗證 magic bytes、實際格式、width、height、pixels 和 payload bytes。temporary stream/
decoder/buffer 均在使用範圍以 `using`/`finally` 釋放；不使用 generic `object` 或讓 `JsonElement`
穿越 normalizer。

metadata cache 不快取 image 或使用者資料。若加入 cache，cache key 必須含 server-resolved profile alias
與 generation，並只保留 copied immutable option DTO array，具有固定 entry/byte/TTL 上限與 generation
替換時的淘汰機制。因既有 executor contract 沒有將 generation 帶入 `ConnectorOperation`，本 P7.3
優先維持 connector request-local metadata projection；generation-scoped long-lived cache 必須先由
composition root 提供可證明的 retirement callback，否則 fail closed 不快取。

weekly statistics 以固定 `QueryExpression`（而非 caller FetchXML）實作既有查詢的已證實投影與
active/Sunday/filter/order。page `EntityCollection`、paging cookie、temporary list 與 byte counters
只存在 connector call；任何 `MoreRecords` 但 cookie 缺失、超過頁/列/byte 上限、欄位型別錯誤或取消
都拋出，外層不得建立 partial response branch。

## 相容、啟用與回復

P7.3 僅新增 disabled local capability。所有 ChurchReport consumer、legacy code、feature gate 和
deployment settings 都不變，因此本次變更沒有 product traffic 或 CE cutover。P7.4 才能擁有逐能力
feature gate、legacy/Gateway non-overlap、rollback owner 與 CE parity evidence。

在本 task 發生 connector failure、timeout、cancellation、read-back mismatch 或 cleanup uncertainty 時，
operation 回傳 bounded failure，lease 走既有 deterministic dispose/fault eviction。不得 retry 影像寫入；
本機單元測試可用 fake service 證明這些規則，但不能充當 CE evidence。

## 測試策略

1. 先以 RED tests 鎖定 IDs、registry definitions、response union one-branch validation 和 executor
   pre-admission rejection。
2. Data8 tests 用短生命週期 fake `IOrganizationService` 驗證固定 entity/columns/query、image readback、
   metadata projection、bounded pages/cookie/bytes，以及 client disposal/fault。
3. ProductClient tests 驗證 request cloning、closed validation、executor request、response ID/CE/branch
   mismatch rejection，以及 response image defensive copy。
4. 在 task 邊界跑完整 solution Release test/build、encoding/CRLF、diff/scope 和 isolation/lifecycle gate。

## 已知證據限制

這份設計不能宣稱 CE 8.2/9.1 parity、image server size/format parity、deployment binding、consumer
migration、feature-gate enablement 或 ToolUtility removal。它們在 matrix 中維持 `evidence-pending` 或
`consumer-not-migrated`，由後續 task 取得各自所需的受控證據。
