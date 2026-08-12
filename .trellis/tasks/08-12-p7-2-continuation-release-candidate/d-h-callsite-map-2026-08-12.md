# P7.2 continuation Slice D–H 實際呼叫點地圖

本文件是 2026-08-12 的只讀盤點結果。它把 coverage matrix 的 operation ID 對齊現有
ChurchReport legacy 實作，用於定義新的本機 capability 邊界與測試；它不授權使用、修改、
啟用或重試任何 legacy CRM 寫入。

## Slice D：donation lifecycle

| Call-site | 現有入口與 legacy 邊界 | 本機 capability 的安全界線 |
| --- | --- | --- |
| `00036` fee update after payment | `DonationPaymentReturnWorkflow` → dispatcher → `DonationFeePaymentProcessor`，Factory `ToolUtility` 依序更新 fee、stor lesson、contact card。 | 固定 fixture key + server-owned payment transition；exact projection、idempotency、uncertain timeout 不 replay。 |
| `00037` recurring completion | `RecurringDonationPaymentProcessor` 可在 fee 建立後才更新 booking。 | 只定義具名 completion plan；未來 CE executor 必須擁有 multi-step ledger 與補償。 |
| `00038` contact create/update | `DonationPaymentManager` → `DonationContactService`，舊流程組開放 attribute bag。 | 拒絕開放 attributes，僅允許 named fixture/mode contract。 |
| `00042` cancel booking | `DedicationController` 呼叫未 await 的 cancel async flow，且會呼叫外部維護服務。 | 固定 transition + server dedup；不得捕捉 request state 或自動 retry。 |
| `00043` contact with dedication number | `DonationContactCreationService` 的 number allocation 為讀取後遞增模式。 | number allocation 必須由未來 server capability 原子擁有；本機層不推導或製造號碼。 |
| `00049` card profile | Session-cached card list 序列化後更新 contact。 | 不接受 raw card/token；只保留 opaque fixture 與 masked mode。 |

## Slice E：appointments

`AppointmentController` → session-cached `AppointmentsListManager` → Factory `AppointmentsDownUpLoader`。
舊 create、assign、schedule 與 delete 可能部分完成；本機版本只能建立具名 plan，owner 必須由
未來伺服器端 authorization 導出，不能來自 UI 或 session cache。

## Slice F：contact onboarding

`NewPersonController` → `NewPersonModel` → `NewPerson.CreateNewContactFromView` 依序執行
contact create、owner assign、list membership、present record、LINE notification；Controller 另有 detached
background task。local-only contract 只接受 `fixtureGraphKey`，不保存 session/principal，不建立背景
工作；未來治理 executor 必須以 present record → membership → contact 的 reverse-known-key 順序 cleanup。

## Slice G：fee lessons

`FeeManagementController` 的 stage 與 batch save 使用 session-held `FeeList`／`ChangeHistory`。更新
stor lesson 後更新 fee、或 create fee 後 assign owner 的 legacy sequence 都可能部分完成。local-only
draft 必須 per-operation、immutable、bounded 及 discard；金融 CE 寫入仍須 fixture preimage、exact
projection、reconcile 和 cleanup 才可建立。

## Slice H：attendance

Download helper（`00068`）與 upload create/update（`00069`）目前仍依賴 `ToolUtility`；upload 路徑
會連帶變更 contact、owner、group 與 follow-up，因此不能視為單一安全 upsert。新的本機契約只接受
attendance key、week start 和 present state：zero-active 不關聯週報、exactly-one-active 精確關聯、
duplicate/unavailable fail closed。此階段不建立、修改或刪除週報／出席紀錄。

## 共通禁止事項與測試責任

- 新本機層不得讀取或保存 `Session`、`HttpContext`、principal、ToolUtility、connector、CRM service、
  owner、endpoint、credential、token 或 profile。
- 每個 CE operation 維持 `CeExecutorEnabled=false`、`ConsumerEnabled=false`，Data8 executor 必須在
  admission、lease、client 建立前回傳 `operation.not-supported`。
- 測試必須覆蓋完整 allowlist、未知／額外 authority 欄位、null/empty/oversize value、A/B 同時操作、
  timeout no-replay、partial completion no-go 和 deterministic local discard。真實 CE read-back／
  cleanup 是日後每個 Slice 獨立 governed cycle 的必要條件，不能被本機測試取代。
