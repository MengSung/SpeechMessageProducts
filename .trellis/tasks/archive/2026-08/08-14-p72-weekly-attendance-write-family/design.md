# P7.2 週日出席與週報寫入能力家族：技術設計

## 邊界

既有 QR 流程把「讀取聚會統計」與「出席寫入、關聯、週報重算、通知」混在持有 CRM `Entity` 的
可變 utility object。新 family 不得把 Package03 read DTO 反向轉回 `Entity`，也不得以 read-new/write-legacy
方式隱藏仍有的 mutation。應依下列順序拆分：

```text
server-authenticated request scope
 -> server-owned QR/attendance command normalizer
 -> fixed operation ID + immutable bounded command
 -> P7.2 admission/idempotency/ledger boundary
 -> generation-owned Data8 lease
 -> one explicit CE mutation operation
 -> exact-ID read-back/reconcile
 -> deterministic reverse-known-key cleanup when task-owned fixture applies
 -> immutable, de-identified result
```

每個步驟都在 server-derived `(subject, product, authorization scope, profile alias, generation)` 邊界內執行。
不得保存 `HttpContext`、CRM entity、paging cookie、credential、QR input、ledger mutable state 或 response 在
singleton/static/cache。任何 timeout、cancellation、fault 或 cleanup uncertainty 均使 lease 不可復用，且該
mutation family no-go/no-replay。

## 初步 capability 拆分

| capability | 現行副作用 | 最早可安全實作的前置條件 |
| --- | --- | --- |
| meeting-statistics locate | fixed Sunday read | 已有 Package03 read；只能當 command precondition，不能當授權或寫入成功證據。 |
| present-record locate/create | lookup + possible Create | server authorization、stable idempotency key、task-owned fixture、exact pre/post graph。 |
| attendance field update | time and attendance flags Update | fixed field set、atomicity/idempotency semantics、read-back projection。 |
| meeting relationship update | present-record lookup Update | fixed relationship target、exact prior state、reverse cleanup plan。 |
| weekly-report recalculation signal | weekly report Update | unique target-list/Sunday resolution、allowed aggregate semantics、read-back and rollback owner。 |
| notification | out-of-band side effect | separate post-commit idempotent delivery contract；不與第一個 CE mutation slice 混合。 |

任何 caller-controlled dynamic attribute 或 QR token 語意必須先轉為 server allowlist command kind；無法唯一
正規化時應在 lease 前 fail closed。靜態全域 lock 不可視為跨 process／host 的 concurrency authority；若需要
串行化，必須由 deployment-owned bounded coordination 或 CE concurrency/version policy 證明。

## first slice 選擇規則

source audit 後只選擇一項副作用最小、可由全新 task-owned fixture 完整建立與清除的 operation。它必須不依賴
通知、全域 lock、可變 Entity graph 或未知週報重算。若無此操作，交付 precise local no-go、測試與設計，不得
為追求進度製造假的 CE baseline。

## 測試與回復

- RED tests 鎖定 authorization-before-I/O、server-owned command normalizer、idempotency/no-replay、exact
  read-back mismatch、cleanup order、A/B isolation 與 lease fault eviction。
- Fake connector 必須用 distinguishable A/B fixture data，並斷言 command、ledger、response、cache 與 log 都
  不跨 boundary；drain 後 counters 回到 baseline。
- CE 寫入 only after new family preflight=go。它的 rollback 僅可觸及 ledger 中精確 task-owned IDs；對
  timeout/ambiguous operation 不執行推測性 retry 或 cleanup mutation。

## 證據限制

本 task 的 planning 或 local tests 不代表 CE、consumer cutover、host parity、traffic、P7.5 removal 或 P8
completion。所有 checked-in gates 維持 false。
