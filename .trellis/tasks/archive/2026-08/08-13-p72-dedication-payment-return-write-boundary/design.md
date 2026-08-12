# P7.2 定期定額奉獻付款回傳寫入邊界設計

## 設計決策

現有 `RecurringDonationPaymentProcessor.HandlePaymentReturn` 是 legacy product flow，不是可安全重用的
Data8 capability：它先讀取認獻單與第 `001` 期費用，再可能更新 contact 卡片資料、建立 `new_fee`、
讀回新 fee 並指派 owner、更新 booking，最後才送出通知。任一遠端步驟 timeout 後都無法從呼叫端
知道是否已提交。因此新邊界不直接搬運 `Entity` 或呼叫既有 processor；它先將任何未來可測試的
payment-return 意圖限制為 immutable、去識別化的 operation-local plan。

`fees.retrieve.by.dedication.period` 是必要的 dedup read，但只在 server-owned recurring-dedication
capability 完成完整 authorization 與 immutable request assembly 後才能使用。瀏覽器、provider 或
legacy processor 的 `ProductEntityId`、Owner、profile、endpoint、credential、token、card data、
Entity 與 attribute bag 都不是此邊界的 authority，也不會進入 local-only DTO。

## 寫入家族與不可混合的責任

| 家族 | 現有 legacy 行為 | 未來受治理 operation | 本 child 的 local-only 責任 | CE 前置條件 |
| --- | --- | --- | --- | --- |
| Dedup read | booking + paid period `001` fee 查詢 | `fees.retrieve.by.dedication.period` | 只定義「complete / matching processed」觀察 | server authorization、固定 projection、exact read-back |
| Card profile | 以 raw card/token 組合後更新 contact | `payments.contact.update.card.profile` | 僅接受 opaque fixture key 與 masked mode | task-owned contact baseline、single update、read-back、restore |
| Fee create | 建立 `new_fee` | 尚未可 dispatch 的 `payments.fee.create.recurring` family | 不建立 fee entity、只讓 fee-update plan 保持 local | fresh booking/contact fixture、idempotency key、create read-back、known-ID delete |
| Fee owner | 新 fee 建立後才 Assign | 與 fee create 分開的 owner-derived assignment | 不接受 caller Owner 或同-user assign 降級 | server-derived distinct owner、assignment read-back、rollback |
| Booking completion | 更新 paid period/status/explain | `payments.dedication.complete.recurring` | 固定 transition name，不能帶欄位 map | booking preimage、single update、exact projection restore |
| Notification | LINE 發送 | 不屬 CRM mutation family | 不加入 CE plan 或 cleanup | 另有 provider-safe outbox/idempotency 設計 |

上表中的 `payments.fee.create.recurring` 僅是本 child 的未來 family 名稱，不加入 `OperationIds`、
registry 或 executor，避免未經 review 的 local string 被誤認為已可 dispatch capability。

## 資料流與隔離

```text
payment callback
  -> provider normalization (既有產品邊界)
  -> server-owned recurring payment authorization / immutable request assembly (未來 child)
  -> exact governed reads: booking + fee-period dedup + allowed projections (未來 child)
  -> P72DonationPaymentLocalDecision (pure, local-only)
  -> P72DonationPaymentLocalPlanBuilder (pure, local-only)
  -> [目前結束：CE dispatch=false, consumer=false]
  -> 未來獨立 governed CE family：preflight -> provision -> one dispatch
       -> exact read-back -> reconcile -> reverse-known-key cleanup
```

所有 mutable observation 僅存在當前呼叫堆疊。計畫建立器防禦性複製 allowlisted 值；不保存 request、
principal、profile、client、lease、cache entry、Task、timer、subscription 或 `HttpContext`。這使 A/B
交錯請求不會跨使用者、跨 profile 或跨 product 傳遞任一 payment/CRM state。

## 終態與不可重播規則

| 條件 | local disposition | 是否可以建立 local plan | 後續規則 |
| --- | --- | --- | --- |
| complete + succeeded + 未處理 + awaiting | `PrepareFutureGovernedDispatch` | 可以，但 CE/consumer=false | 只可等待全新 governed family；不是 write 授權 |
| complete + succeeded + 已處理/非 awaiting | `AlreadyProcessed` | 不可以 | 不重播 |
| complete + failed | `RequireReconciliation` | 不可以 | 不自動修改 CRM，不重播 |
| pending/unknown/incomplete/timeout/ambiguous/partial | `NoGo` | 不可以 | 停止該 family；只可讀取診斷與記錄 |
| CE read-back mismatch/cleanup uncertain（未來） | 不可發布 terminal no-go | 不可以 | 停止 mutation family、不得 retry、不得進下一 Slice |

## 新 CE family 的硬性前置條件

在本 child 之外，且只有所有本機 gate 綠燈後，才可建立新的 governed child。其唯一合法順序為：

```text
new child + new nonce + new ledger + task-owned fresh fixture
  -> read-only preflight=go
  -> provision=go
  -> one allowlisted dispatch
  -> exact read-back + reconciliation
  -> deterministic reverse-known-key cleanup
```

任何 timeout、ambiguous、no-go、read-back mismatch 或 cleanup uncertainty 都停止整個 mutation
family。歷史 Slice C cycle 不是此流程的輸入，不能以修正本機程式作為重試理由。

## 風險與明確 no-go

- 目前 legacy flow 把 callback 的 `ProductEntityId` 直接轉成 `Guid` 並讀 booking，且仍持有 SDK
  `Entity`；在完成 server-owned authorization / immutable request builder 前，不可切 consumer。
- `CreateFee` 後才 assign owner，表示 create/assign/booking update 的 partial completion 可能各自發生；
  不可用一個 generic `Update(Entity)` 或 transaction 假裝可回滾。
- `new_visa_info` 組合原始 card/token 意義不明，也沒有受控的遮罩/retention contract；card update
  family 必須獨立，不能混入 fee lifecycle 的 first CE cycle。
- notification 可能在 CRM 寫入後失敗；它不屬 CE cleanup，必須等待另一個具 outbox/idempotency
  設計的產品邊界，不能以重播付款回呼補送。
- 因上述缺口，P7.4 consumer、P7.5 ToolUtility removal 與 P8 全部維持 no-go；但不阻擋本 child 的
  local TDD、P7.4 其他獨立 read-only batch 或 repository-side deployment documentation。
