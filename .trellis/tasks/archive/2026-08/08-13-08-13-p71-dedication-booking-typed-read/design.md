# P7.1 認獻單強型別讀取能力設計

## 架構決策

`ORG-CALL-00041` 的資料層被拆成一個獨立 Package01 read capability，而不是讓 consumer 直接
組 FetchXML 或由 P7.4 修改既有 `DonationBookingService`。邊界如下：

```text
future P7.4 server-authorized consumer (not in this child)
  -> IPackage01DedicationBookingReadClient
  -> IDynamicsOperationExecutor
  -> Data8ProfileOperationExecutor / Gateway executor
  -> Package01Data8ReadOperations
  -> one bounded RetrieveMultiple projection
  -> OperationResponseData.DedicationBookingRecords
  -> request-local ProductClient DTO copy
```

每一層只接收封閉型別。瀏覽器輸入、session identity、profile 選擇與 authorization 不是此 P7.1 client
的責任；未來 consumer 必須在呼叫前從 deployment 與 server scope 推導它們。本 child 也不會讓 caller
提供 CRM query、logical name、column set、owner、endpoint、credential 或 connector kind。

## 新合約

新增 `OperationIds.PaymentsDedicationRetrieveByContact`，並由 `Package01OperationRegistry` 定義：

- kind：`read`
- template：`payments.dedication.by.contact.v1`
- response kind：`Package01DedicationBookingRecords`
- idempotency：`read-only`
- parameter：`contactId:guid:required`、`contactName:string:optional`
- envelope/page/item bounds：沿用 Package01 已驗證的保守固定上限。

`OperationResponseData` 增加唯一分支 `DedicationBookingRecords`；其 wire record 不得公開任何 CRM
attribute 名稱。記錄只承載先前 `DonationBookingService.MapBooking` 已需要的 scalar，而且允許 null 表示
CRM projection 中不存在的 nullable 值，不允許以非預期預設值猜測資料。

ProductClient 定義同形、獨立的 `DedicationBookingRecordDto` 和
`IPackage01DedicationBookingReadClient.RetrieveDedicationBookingsByContactAsync`。client 在 executor 回傳後
比對 operation ID、response discriminator 與非 null branch，再逐筆建立新 DTO 陣列。返回型別只是一個
新建立的 `IReadOnlyList`；它不緩存也不暴露 wire array、`Entity`、response object、stream、lease 或
transport state。

## Data8 查詢與資源規則

connector 使用固定的 `QueryExpression`：entity、filter、column set、order 與 page settings 全由 server
程式寫死。contact lookup 值由已驗證 `Guid` 寫入 query；`contactName` 只為 P7.1 compatibility parameter，
不改變 query 邏輯。一次 `RetrieveMultiple` 以既有 Package01 page/bytes/cumulative bounds 收集並立即投影。
若頁數、bytes 或 item 上限違反，丟出已存在的受控 bounded failure；不留下 partial records。

Data8 lease/pool 是唯一外部資源 owner。這個 capability 不新增 client pool、cache、timer、queue、stream
或 background work；取消與 fault 繼續交給既有 executor 的 `await using` / fault eviction 路徑。任何
transport 不確定或 cancellation 都不重試，且 connector 不返回到另一個 profile/generation。

## 隔離與錯誤矩陣

| 條件 | 行為 |
| --- | --- |
| 缺/錯 `contactId` 或未知參數 | pool 前拒絕；不配置 connector。 |
| caller 想以 `contactName` 改寫資料範圍 | 忽略其作為 authority；query 只依 typed contact ID。 |
| registry/executor 回傳其他 operation 或 response branch | ProductClient fail closed，不映射也不發佈 rows。 |
| CRM page 值型別不符、資料超限或 projection failure | connector failure；lease 依既有 fail/evict path 清理。 |
| cancellation/timeout | 原樣傳遞、不重試、不回傳部分 list。 |
| A/B interleaving | 每次請求建立新 parameters/wire/DTO collection；無 static/session/cache retained row。 |

## 交付與 rollback

本 child 的 rollback 是「不接 consumer，所有 gate 維持 false」。移除或停用這項 registry/client path 即回到
既有 ToolUtility route；不得用切 connector、CE version、dual-read 或 request-time fallback 取代 rollback。
完成本機驗證後，權威 matrix 最多能記錄 `registry=declared`、`executor=implemented`、
`productClient=implemented`。consumer、CE 8.2/9.1、Embedded/Dedicated runtime evidence 維持原本狀態，
直到各自的受治理 work item 取得實證。
