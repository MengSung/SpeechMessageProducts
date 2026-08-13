# P7.2 週日出席／週報寫入能力家族：本機 no-go

## 結論

此 child 已完成來源、既有 P7.2 本機 contract 與 production QR 入口的交叉稽核。目前不可安全實作
或 dispatch `ORG-CALL-00063` 的新 CE writer family；這是精確的 **local design no-go**，不是 CE
資料庫、Full-Text Search 或測試環境寫入權限的拒絕。

沒有執行 CE preflight、fixture provision、Create、Update、Assign、Delete、Associate、Disassociate、
feature gate、traffic 或 cleanup。歷史 Slice C 的 nonce、ledger、fixture、descriptor 與 evidence 仍未被
讀取為可重用資料，也沒有被重試。

## 已證明可重用的本機契約

先前封存的 P7.2 continuation candidate 已提供以下 local-only attendance contract，且本輪重新執行
targeted tests 驗證 **32 passed、0 failed**：

- `P72AttendanceWeeklyReportDecision`：zero-active 是合法的 unlinked 分支；exactly-one-active
  必須 exact-link read-back；duplicate-active 或 unavailable 為 no-go。
- `P72AttendanceUpsertLocalDecision`：download-create 與 upload-upsert 的 attendance cardinality，
  duplicate、timeout／ambiguous、read-back 不完整皆 no-replay。
- `P72AttendanceLocalPlanBuilder`：只接受固定 operation ID、ISO Sunday 與 bounded present state；
  `CeDispatchAllowed=false`、`ProductConsumerAllowed=false`。

這些 reducer 是下游新 writer 的必要基礎，但沒有 CRM ID、owner、profile、descriptor、ledger、
preimage、postimage 或 cleanup key，因此不能自行升格為 CE mutation 授權。

## 阻斷原因與程式證據

### 1. 授權與識別資料在任何安全邊界之前被寫入共享狀態

`QrCodeController.PersonalQrCodeGetLineId` 先呼叫 `SetupLineContext`，將 browser POST 的
`UserLineId`、`GroupId`、`RoomId`、`ViewType` 寫入 process-wide `InMemoryContext`；然後才建立
`PersonalQrCodeUtility` 並讀取 `InMemoryContext.ListManager.QrCodeId`。`PersonalQrCodeView` 亦把
route supplied QR 值寫入同一個 `InMemoryContext.ListManager.QrCodeId`。

因此目前無法在 QR/session hydration、locator parsing、client composition、CRM lookup 或任何寫入前，
從 request-local、server-derived subject/scope 證明掃描者與目標 contact／meeting 的授權。直接把
Data8/ProductClient 接到這條路徑會把 caller supplied locator 與 shared mutable state 當作 authority，違反
跨使用者／跨 profile isolation contract。

### 2. 單一 QR 呼叫不是單一固定 mutation

`PersonalQrCodeUtility.SetupQrCodeIdString` 依 QR 內容與 current local Sunday 讀取 contact、meeting
statistics 和動態 attribute；接著 `SigningMeetingStatistics` 會依目前資料進行：

1. 查詢或建立 present record；
2. 寫入時間、出席 flags 與 meeting-statistics lookup；
3. 在既有 present record 上更新 weekly report 的 `new_saved_flag`；
4. 在缺資料時呼叫 `WeeklyReportProcessor.CreateWeeklyReportAndPresentRecord`；
5. 依分支發送或準備 LINE notification。

這些副作用沒有共同 idempotency key、single-writer ledger、完整 preimage/postimage、exact graph
read-back、reverse-known-key cleanup 或可證明的 rollback owner。`SundayQrCodeUtility` 也有相同的
write-adjacent 路徑，且使用靜態 `lock`；static lock 不是跨 host/process 的 concurrency authority。

### 3. 現有 Data8 transfer 無法取代 QR attendance writer

`NewPersonContactTransferBetweenLists` 的 Data8 template 已可對其獨立 task-owned fixture 做固定的
membership／present-record／weekly relation／contact list／optional owner graph read-back；但它的輸入
本身含 contact/list/owner IDs，且語意是轉組，不是已授權掃描者的 attendance update。將 QR 流程接到
它會造成 caller-derived target、錯誤的 business mutation 與 read-new/write-legacy 混合，故明確禁止。

## 恢復條件

只可由後續獨立 child 先完成以下 repository-side 設計與 TDD，才可評估新 CE family：

1. 將 QR scan 建立為 request-local、server-authenticated scope；在 parse QR locator、讀 CRM、建 client
   或寫 `InMemoryContext` 前完成 server authorization。browser LINE/group/room/QR 值只能作 locator，
   不可作 subject、profile、owner、endpoint 或 credential authority。
2. 建立固定、bounded attendance command，僅允許單一明確 mutation（例如既有 attendance record 的
   fixed present-state update，或 task-owned fresh present-record create）；meeting lookup、weekly link、
   weekly recomputation、contact/group/owner mutation 與 notification 必須分屬明確的後續 capability。
3. 每個 command 都要有 server-owned operation ID、fresh nonce、single-writer ledger、task-owned
   fixture exact keys、preimage/postimage、idempotency、exact read-back、reconcile 與 reverse-known-key
   cleanup owner。
4. TDD 證明 authorization-before-I/O、A/B user/profile isolation、timeout／ambiguous no-replay、
   duplicate/unavailable weekly report no-go、zero-active unlinked、exactly-one-active exact-link read-back，
   以及 cancellation／lease fault eviction。
5. 僅當上述本機品質閘門通過後，以全新 child、nonce、ledger、fixture 進行一次 read-only preflight；
   preflight=go 才能依核准 allowlist provision 與 single dispatch。

在此之前，最安全且能推進 P7 的工作是處理其他 authoritative matrix family 的獨立 local-only／read
capability，或建立上述 request-local QR authorization boundary 的專屬 child；不得修改這條 legacy QR
路徑、不得啟用 consumer/gate，也不得開始 P7.5 或 P8。
