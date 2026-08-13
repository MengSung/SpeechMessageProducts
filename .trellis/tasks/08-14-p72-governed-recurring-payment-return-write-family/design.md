# P7.2 受控定期奉獻付款回傳寫入家族技術設計

## 設計決策

採用「先建立單一 writer 的受控 cycle admission，再接入 CE」的方案。第一個 vertical slice 是
`payments.fee.update.after.payment`；它只定義 fee update 的未來 governed dispatch，不偷帶 fee create、
owner assign、booking completion 或 notification。這是唯一能避免 legacy flow 的 partial side effects 被
錯誤當成一個 transaction 的設計。

下列替代方案已排除：

1. 直接改寫 `RecurringDonationPaymentProcessor`：會混合 legacy CRM SDK 與新 typed path，無法證明
   single writer、idempotency 與 cleanup，故不採用。
2. 把 create／assign／booking update 放進 generic `Update(Entity)` batch：會把每個 mutation 的
   read-back／rollback boundary 抹平，並讓 timeout 後狀態不明，故不採用。
3. 複用舊 Slice C fixture／ledger：違反 historical no-replay 與 family isolation，故不採用。

## 邊界與資料流

```text
provider callback / legacy workflow（未接線）
  → server-owned normalized observation（未含 CRM ID、Owner、credential、token）
  → P72DonationPaymentLocalDecision（pure）
  → P72DonationPaymentLocalPlanBuilder（pure；CE/consumer=false）
  → P72GovernedPaymentCycleAdmission（pure；驗證 fresh descriptor 與 stage）
  → 未來受控 executor（僅 preflight=go 時建立）
      bootstrap → read-only preflight → provision → one allowlisted dispatch
      → exact read-back → reconcile → reverse-known-key cleanup
```

`P72GovernedPaymentCycleAdmission` 不持有 CRM client、endpoint、credential、profile、Owner、Entity、
Session 或 HttpContext。它只接收 immutable scalar stage evidence，輸出 bounded disposition；因此 A/B
request 或多個 test cycle 無法共享 mutable state。任何實際 CE I/O 都必須由 future executor 的 scope
owner 建立、lease、dispose，不能由此 local contract 偷偷取得。

## Cycle state machine

| 階段 | 最低條件 | 允許下一步 | 終止情形 |
| --- | --- | --- | --- |
| Bootstrap | 新的非空 nonce、family binding、descriptor digest、空 ledger | Preflight | binding 不完整／歷史 binding → no-go |
| Preflight | 零 mutation；全部固定分類為 `go` | Provision | unavailable／duplicate／unauthorized／baseline-unprovable → no-go |
| Provision | 本次 ledger 寫入 exact created IDs 與 marker | One dispatch | timeout／unknown create outcome → no-go；只可 reconcile known key |
| Dispatch | 恰好一次 allowlisted mutation、不可 retry | Read-back | timeout／ambiguous／partial → no-go |
| Read-back | exact scalar projection 符合 expected postimage | Reconcile | mismatch／unavailable → no-go |
| Reconcile | 已確定所有 known effects | Cleanup | 任何 unknown effect → no-go |
| Cleanup | 反向順序處理 ledger known keys 並 read-back absent／baseline restored | Complete | cleanup uncertain／failure → no-go |

每一個 `no-go` 都設定 `ProhibitsReplay=true`。不完整的 local state 不可轉成 `go`；只有新 family、
新 nonce、全新 ledger 與全新 fixture 才能構成另一個獨立 cycle。

## Descriptor、ledger 與 allowlist

新的 descriptor 不可參考舊 Slice C 存檔或任何 shared data。它在 future governed child 中必須含：

- family name 與 immutable descriptor digest；
- non-empty nonce；
- 本次 fresh booking／contact／fee 的 exact IDs（只在 local secured ledger，不輸出到 diagnostics）；
- fixture marker 與 expected preimage/postimage digest；
- 固定 mutation allowlist：第一 slice 僅可執行 fee update；
- server-derived owner binding；沒有明確、已啟用且 distinct 的 descriptor owner 即 preflight no-go；
- cleanup order：先還原 fee preimage，再刪除由本 ledger 建立的 fee，最後清除 fresh dependent graph。

`P72GovernedPaymentCycleAdmission` 只驗證 descriptor 的 safe local shape，不自行產生 ID、不掃描 CRM、
不選 Owner。真正 fixture provisioner 必須在每次 Create／Update 前後寫入 single-writer ledger，並在
transport uncertain 時保留 exact pending state，讓 executor 停止而不是 retry。

### 本機 payment fixture 控制面

在真正 executor 之前，`P72PaymentFreshFixtureControlPlane` 是 payment 專用的第二道 pure-local
boundary。它只接受去識別化完整性 evidence：固定 schema version、fresh nonce、immutable descriptor
digest、empty single-writer ledger、secure exact-key ledger、server-derived distinct owner binding、fee-update
only allowlist、fixed exact projection 及 reverse-known-key cleanup plan。它不保存上述敏感值，也不產生
CRM ID、Owner、profile、endpoint、credential 或 fixture marker。

control plane 的輸出只有兩種：完整 bootstrap 的 `ReadOnlyPreflightRequired`，或固定分類的 `NoGo`。
即使完整，也不會直接開放 provision、dispatch、CE executor、ProductClient consumer、feature gate 或
traffic；future executor 仍須將其結果與 `P72GovernedPaymentCycleAdmission` 的 fresh bootstrap 和
preflight=`Go` 交叉驗證。這避免「有一份 local plan」或「有一份 descriptor」被誤當成付款寫入權限。

`P72PaymentFreshFixtureControlPlane` 的 allowlist 固定是 `FeeUpdateAfterPayment`，且 operation ID 固定為
`payments.fee.update.after.payment`。fee create、owner assignment、booking completion、contact card profile
與 notification 不存在於 input、result 或 enum；要加入任一項必須先建立新的 writer child 與自己的
descriptor／ledger／preflight／read-back／cleanup 契約。

## Transaction 與 idempotency

CRM 不可假設跨 create／assign／update 的原子 transaction。因此每個 mutation 需以「一個 allowlisted
operation + exact preimage + exact postimage + known-key cleanup」獨立治理。第一個 slice 不會建立新 fee，
只會在 task-owned fresh fee 上做已定義的 payment-success update；fee create、assign 與 booking completion
各自必須在後續 child 建立獨立 writer slice。

付款 observation 只在 complete succeeded、沒有 matching processed order、仍 awaiting payment 時可形成
future local plan。already processed、failed、pending、unknown、incomplete、timeout、ambiguous 與 partial
一律不形成 dispatch plan，並以 `AlreadyProcessed`、`RequireReconciliation` 或 `NoGo` 表達。

## Read-back、reconcile、rollback 與通知

read-back 必須使用 fixed typed projection，精確比對本 task 的 expected fee status／paid-period fields，
不以任何全域搜尋或模糊名稱確認。reconcile 只讀 ledger known IDs；如果不能證明是否套用，結果是 no-go。
rollback／cleanup 由 ledger owner 依 reverse-known-key order 執行，並再以 read-back 證明恢復或 absent。

LINE notification 不屬於此 CRM writer slice。它必須等待 CRM family 已完成 exact reconciliation 的另一個
outbox/idempotency design；不可從 controller 或 background task 直接觸發。

## 安全、隔離與資源生命週期

- caller 不能指定 CRM ID、Owner、profile、connector、endpoint 或 credential；server composition root 是唯一
  authority。
- 任何 temporary directory、ledger stream、process、cancellation registration、Data8 lease／client 都必須由
  受控 executor 使用 `try/finally` 或 `await using` 管理。timeout／ambiguous transport 的 client 不能重用。
- diagnostics 僅回傳固定分類；不記錄 CRM IDs、名稱、Owner、endpoint、token、cookie、baseline、raw response
  或 raw exception。
- 所有 DTO／local plan 都防禦性複製並限界；不得存入 singleton、static cache、Session 或 shared collection。

## Rollout 與回復

本 child 不改 feature flag、不接入 consumer、不切換流量。local-only contract 的回復即保留
`CeDispatchAllowed=false`、`ProductConsumerAllowed=false`，且刪除／不註冊任何 future executor。任何 CE
cycle no-go 都不影響其他 P7 slices，但它阻止本 family 的後續 CE writes，直到另一個新 child 有新的
fixture family 與 fresh evidence。
