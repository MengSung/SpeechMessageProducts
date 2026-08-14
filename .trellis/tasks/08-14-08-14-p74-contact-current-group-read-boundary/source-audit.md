# ORG-CALL-00052 來源稽核

## Matrix

| 欄位 | 結果 |
|---|---|
| Call site | `ORG-CALL-00052` |
| Operation | `contact.current.group.retrieve` |
| Legacy entry point | `ContactService.GetContactCurrentGroup` |
| Matrix kind | read / personal-data |
| Existing registry/executor/client | 未建立 |
| Consumer | 未遷移 |
| CE／host／traffic | evidence-pending；本 child 未執行 |

## Source trace

`ContactService.AddContactToListAsync` 在檢查既有聯絡人目前小組時，將可變的
`Entity existingContact` 傳入 `GetContactCurrentGroup`。後者呼叫
`ToolUtility.QueryListOfContactManyToMany(contact.Id)`，逐筆讀取 `EntityCollection`，
並在第一筆 `new_app_named=true` 時立即回傳。沒有 operation-specific page/row/byte budget，
也沒有零筆／一筆／多筆的明確 ambiguity policy。

同一 caller 隨後可能執行：

- 加入目標名單或從來源名單移除；
- 建立出席紀錄；
- 更新 contact 的 `new_cell_list_contact` 關聯；
- 指派 Owner；
- 發送 LINE 通知。

因此該讀取不是目前 transaction 的獨立 consumer；把 ProductClient read 接到這個方法只會
形成「Gateway read + legacy write」混合路徑，無法證明原子性、idempotency、rollback 或
跨使用者隔離。

## 判定

**`source-only-local-design-no-go`。**

這不是 CE 連線、全文檢索、測試資料權限或 P7 全域阻塞；本 child 未進行任何 CE、fixture、
mutation、gate、traffic 或 cleanup。它只阻止 ORG-CALL-00052 的直接遷移。

## 限時 CCG 分析

專案 self-healing runner 在 45 秒模型等待預算內取得 Gemini architect output。其結論與本機
trace 一致：沒有 request-local authorization、first-match 是非確定語意，且 read 結果驅動
多個寫入與通知；決定為 `SOURCE_ONLY_LOCAL_DESIGN_NO_GO`。Claude 無 usable output，故記錄
`雙模型未完成`；本 child 未因此重試等待或降級安全門檻。

## Recovery conditions

1. 先建立 authenticated-principal → immutable request-local authorization scope，且不依賴
   Session、shared `InMemoryContext`、保存帳密的 ListManager 或 caller-supplied Entity。
2. 另建立固定 query/template 與 bounded DTO；多個 app-named memberships 必須回傳
   `ambiguous`，不得猜第一筆。
3. 將 current-group read 與 membership/attendance/contact/Owner/notification mutations
   拆成各自有 idempotency、read-back/reconcile、rollback 與 cleanup owner 的 capability。
4. 補齊 A/B isolation、cancellation、timeout/fault cleanup、gate=false zero-I/O 及
   CE/Embedded/Dedicated parity evidence 後，才可重新評估 consumer cutover。
