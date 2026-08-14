# P7 Runtime Health WhoAmI ProductClient Boundary 設計

## 資料流

deployment-owned profile alias + workload subject
→ `IRuntimeHealthWhoAmIClient.CheckAsync`
→ fixed `OperationIds.RuntimeHealthWhoAmI`
→ existing `IDynamicsOperationExecutor`
→ closed `OperationResponseData.WhoAmI`
→ defensive-copy immutable health identity DTO。

此 client 不組成 Connector、HTTP handler、lease、pool 或 CE request；這些資源仍由注入的 executor 擁有。client 是可安全 singleton，唯一欄位只能是 executor reference；每次呼叫建立短命 request/DTO，完成或 fault 後不保存任何 profile、workload 或 response。

## Contract

- `CheckAsync` 接受有限長度、非空 profile alias 與 workload subject；這兩個值仍要由 composition root／executor 的 deployment policy 驗證，不能由 browser 作為 authority。
- client 固定沒有 parameters、沒有 idempotency key，也不接受 operation ID、CE version、connector、endpoint 或 credential。
- 僅接受 exact operation ID、CE `9.1`、`WhoAmI` response kind 和 non-empty `WhoAmI` branch。三個 GUID 必須完整、非空；不完整或錯誤 branch 拋出固定 contract failure，不能回退 legacy。
- product DTO 只複製 identity scalar；不持有 upstream response 或 transport reference。不得記錄 GUID、profile、workload 或 error body。

## 相容性與回復

這是 additive local-only capability。若 gate/consumer 尚未存在，沒有 request 會叫用此 client；移除本 child 的 ProductClient files 與 registration 即可回到原狀。它不變更 ToolUtility `ValidateConnection`、ChurchReport、registry、Data8 executor 或現有 CE profile。
