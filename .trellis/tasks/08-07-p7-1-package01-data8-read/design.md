# P7.1 Package01 Data8 Typed Read Design

## 邊界

`IPackage01FeeReadClient` 保持產品唯一入口。它送出既有 `OperationExecutionRequest`，
`Data8ProfileOperationExecutor` 只在 Profile resolver 已確認 `ConnectorKind.Data8` 後，建立封閉
`ConnectorOperation`、取得單次 lease，並將 connector scalar map 投影成既有 `FeeRecordDto` 或
`StorLessonRecordDto`。產品與 Gateway HTTP 層均不可看到 Data8 client、template、endpoint、credential
或 raw FetchXML。

## Capability 表

| Operation | Request owner | Response owner | Data8 支援 | Consumer |
|---|---|---|---|---|
| `fee.dedication.retrieve.by.contact` | Registry typed parameters | `FeeRecordDto` | P7.1 | disabled |
| `fee.dedication.retrieve.by.contact.date.range` | Registry typed parameters | `FeeRecordDto` | P7.1 | disabled |
| `fees.retrieve.by.dedication.period` | Registry typed parameters | `FeeRecordDto` | P7.1 | disabled |
| `fees.editor.load.by.disciplelesson` | Registry typed parameters | `StorLessonRecordDto` | P7.1 | disabled |
| `lessons.stor.retrieve.by.contact` | Registry typed parameters | `StorLessonRecordDto` | P7.1 | disabled |
| `lessons.stor.retrieve.by.disciplelesson` | Registry typed parameters | `StorLessonRecordDto` | P7.1 | disabled |

## 執行與生命週期

1. 在 resolver、ConnectorKind、operation ID 與 parameter shape 全部通過前，不解析 Pool 或建立外部資源。
2. executor 將 immutable Profile timeout 轉成 operation deadline；Pool/lease 是 permit、client、deadline CTS
   的唯一 owner。`await using` 覆蓋成功、取消、timeout、connector failure 與 invalid projection。
3. 每個 template 只能由 Registry ID 對應；caller 不能傳 entity、FetchXML、profile、connector、CE version
   或 endpoint。未知／不支援組合 fail closed，絕不轉送 Worker 或其他 connector。
4. Projection 驗證 operation ID、CE version、最大筆數/bytes、required scalar key 與 profile isolation；失敗
   時先 `MarkFaulted` 再離開 lease scope。

## 模式、evidence 與 rollback

Embedded 和 Dedicated 都注入相同 Data8 executor；hosting mode 只是 transport composition，不得改變
operation、ProfileAlias 或 ConnectorKind。P7.1 僅驗證 offline / host-contract parity，consumer flag 不開。
CE evidence 為 `evidence-pending` 直到每個已選 Data8 profile 的 sanitized read handoff 成功；若外部
access 尚未可用，保留 legacy consumer path 並停止在 evidence gate，不回退 P6。
