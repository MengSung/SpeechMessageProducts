# ORG-CALL-00033 source audit

## 結論

`ORG-CALL-00033` 不可在目前程式碼狀態成為獨立、安全的 P7.4 relation-goal
typed-read capability。此為獨立的 source-only local design no-go；它不代表
所有 P7 工作停止，但會阻止此 operation 的 registry、Data8、ProductClient 與
consumer cutover。

## 證據

| 層面 | 目前證據 | 為何 no-go |
| --- | --- | --- |
| 授權來源 | 三個 caller 均以 `GetAccess` / `CanViewContactsBatch` 走 MemberInfo access | `GetAccess` 信任 Session cache；miss 時從共享 `InMemoryContext` 推導並回寫 Session，不是 server-derived immutable scope。 |
| Shepherd scope | `GetShepherdContactIds` 呼叫 `EnsureShepherdListsLoaded` | 未載入時會用 shared ListManager 保存的帳密呼叫 `SetupListManager`，使 credential-backed legacy load 早於新 capability boundary。 |
| request shape | `BatchRelationGoals` 對每個 contact chunk 做 `connection` OR query | contact IDs 雖在 legacy flow 後篩選，但不能被安全重新詮釋成 Gateway 授權輸入。 |
| response bound | `RetrieveAllEntities` 在 `MoreRecords` 時持續翻頁 | 無 query-specific page/row/text/byte 上限，relation graph 可能無界擴張。 |
| error semantics | `BatchRelationGoals` catch 所有 exception 後繼續格式化 | connection unavailable、timeout、partial read 和確實無 relation 被混為 empty string，無法提供 fail-closed typed result。 |

## 精確恢復條件

1. 先建立與驗證 MemberInfo authorization boundary：已驗證 principal 產生
   request-local immutable Church/Shepherd scope，且 scope 建立早於 Session、
   shared context、cache、legacy loader、profile/client composition 與 CRM I/O。
2. Shepherd membership scope 改為 server-owned bounded authorization query 或
   dedicated service；禁止復用 shared `ListManager` 的 credential 或 loader。
3. 另建 relation-goal child：只接收上游 scope 產生的 bounded distinct IDs，
   有固定 query/projection、chunk/page/row/text/byte budgets、immutable DTO 和
   no partial publication 的 error union。
4. 完成 A/B scope/profile isolation、cancellation/fault lease disposal、CE 9.1、
   Embedded/Dedicated parity、rollback 及受控 rollout evidence 後，才可評估
   consumer enablement。

## 排除事項

沒有 CE 操作、fixture、gate、consumer、設定、matrix、ToolUtility、P7.5 或 P8
變更。歷史 P7.2 Slice C 未被重試或引用為本 child 的證據。
