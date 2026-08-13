# P7.2 付款回傳受治理寫入控制平面審計

## 審計範圍與結論

本文件是唯讀、去識別化的 repository 研究記錄：未執行 CE、未建立 fixture、未寫入 ledger，亦未改變任何 production 或測試程式。結論如下。

1. **不得重用既有 Slice C 的具體 descriptor、nonce、fixture、ledger、檔名、環境變數或任何已封存 evidence。** 歷史 Slice C 已以 `write-not-committed` 的終態關閉並完成 cleanup；其 nonce、ledger、fixture 與 descriptor 均不可重播或復用（`.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/rebaseline-summary.md:27`；`.trellis/tasks/archive/2026-08/08-13-p72-dedication-payment-return-write-boundary/prd.md:12-18`）。
2. **不得將 legacy `RecurringDonationPaymentProcessor` 接入新 writer，也不得以 local-only plan 當作 CE 寫入授權。** 前者混合多個不可原子化的副作用；後者明確關閉 CE executor 與 product consumer。
3. 第一個受治理 vertical slice 必須嚴格限於 `payments.fee.update.after.payment` 的一次 allowlisted fee 更新。fee 建立、owner 指派、booking 完成、聯絡人卡片資料與通知都必須是後續、獨立的 writer/outbox 工作，不可偷渡至本 slice。

## 既有付款回傳呼叫鏈與邊界

目前 legacy 路徑為：

```text
PaymentReturnController.ReturnCore
  -> IPaymentGateway.ParseCallbackAsync / QueryPaymentAsync
  -> DonationPaymentReturnWorkflow.HandleReturn
  -> DonationPaymentProductWorkflowDispatcher.HandleDedicationBookingReturn
  -> RecurringDonationPaymentProcessor.HandlePaymentReturn
```

- Controller 解析 callback、再向 provider 查詢付款狀態，最後把結果交給 payment-return workflow（`SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs:97-153`）。
- Workflow 以 provider 資料建立結果；其中 `ProductEntityId`、付款分類與其他欄位都由 provider data 映射而來（`SpeechMessageProducts.ChurchReport/Payments/DonationPaymentReturnWorkflow.cs:172-205`），再按分類分流（同檔:68-84）。
- Dispatcher 對 recurring dedication branch 建立並呼叫 legacy processor（`SpeechMessageProducts.ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs:85-97`）。
- Legacy processor 將 `paymentResult.ProductEntityId` 用作 booking 查找起點，並以期間查詢作為既有 fee 的判斷（`SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs:167-253`）。這不是付款專屬、伺服器驗證的授權邊界；外部 callback/provider observation 不得成為 CRM entity、owner、profile、endpoint 或 credential 的 authority。

legacy processor 的單次處理還會更新 contact 卡片資料（同檔:258-281）、建立 fee（:300-301）、更新 booking 狀態與說明（:303-345、377-384），並發送通知（:348-355、386-387）。這些遠端副作用沒有顯示為由單一 task ledger 管理的 transaction、精確 read-back、reconciliation、reverse cleanup 或 timeout-after-dispatch 的 no-replay 策略。因此任何 timeout、取消或傳輸不確定都不能安全重試，也不能把此鏈當作新 control plane 的 executor。

## 現有 local-only contract：可保留 admission 概念，不是 executor

`P72DonationPaymentLocalDecision.Resolve` 是純本機決策：只有 observation 完整、成功、沒有相符已處理訂單且仍待付款時，才傳回 `PrepareFutureGovernedDispatch`；失敗回傳 `RequireReconciliation`；不完整、pending 或 unknown 一律 no-go（`SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalDecision.cs:198-226`）。

`P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment` 只產生帶有 `fixtureKey` 與固定 `payment-succeeded` transition 的 local plan（`SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalPlanBuilder.cs:85-112`）。catalog 雖標示單一 allowlisted dispatch 與 exact projection / no-replay / reverse-known-key 等政策名稱，卻明確設定 `CeExecutorEnabled=false` 與 `ConsumerEnabled=false`（`SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs:203-210,325-350`）。測試也直接鎖定兩個旗標皆為 false（`SpeechMessage.Dynamics.Tests/P72DonationPaymentLocalDecisionTests.cs:32-55,326-346`），並驗證 A/B observation 與 input snapshot 不共享 mutable state（:283-316、357-390）。

因此此 contract 可作為上游「純判斷／local admission」的輸入，但不能被解讀為 fresh payment descriptor、CRM 寫入 authority、ledger、provisioner、read-back、cleanup 或 live cutover 證據。

## Slice C concrete type 缺口

`P72FreshSliceCFixtureProvisionRequest` 的輸入是五個 static-list、既有 leader、UTC 週期、Data8 service user 與 nonce（`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs:40-49`）。其 request template 只建立 `contact` 和 `list`，並使用固定的 `P7.2-SC-*` marker（:94-201）。provision 流程是三次 create、兩次 list membership execute、一次 assign，之後驗證 weekly-report/list-management graph（:294-406）；cleanup 也只反向處理此 graph（:430-521）。

對應 ledger 的 schema 與 stage vocabulary 均只描述 source contact、leader contact、relationship list、baseline leader 與 list-management stages（`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedger.cs:23-121,341-356,579-591`）。preflight 亦只證明 static lists、task-marked leader、active owner 與 weekly-report cardinality（`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixturePreflightProbe.cs:39-76,127-253`）。

它沒有 payment-family 所需的 fee/booking/contact preimage 與 postimage、付款 idempotency key、fee field allowlist、付款 read-back 或 payment-specific rollback。重用 concrete data plane 會破壞 family isolation，並可能對不相干實體進行錯刪或錯誤還原。唯一可借鑑的是下列**模式**，且必須以全新的 payment-specific schema 實作：每次遠端副作用前持久化 stage、每步精確 ID read-back、傳輸不確定時停止且不重播、僅以已知 key 反向 cleanup、嚴格去識別化 evidence，以及由單一 owner 在 `finally` / `await using` 中釋放 ledger、stream、temporary root、lease、runtime、logger 與 cancellation registration。

## 權威缺口矩陣

`ORG-CALL-00036` 是本審計建議的第一個 operation：`payments.fee.update.after.payment`。矩陣仍標示 registry=`local-only`、Data8 executor=`local-only-rejected`、ProductClient=`not-implemented`、consumer=`not-migrated`、CE 8.2/9.1=`not-executed`，且 rollback 與 rollout owner 都為 pending（`.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json:1335-1370`）。這是未實作缺口，不是可上線能力。

相鄰 recurring-payment writes 也尚未落地：booking complete `ORG-CALL-00037` 與 contact create/update `ORG-CALL-00038`（同檔:1373-1446）、booking cancel `ORG-CALL-00042` 與 dedication-number contact create `ORG-CALL-00043`（:1563-1636）、card profile update `ORG-CALL-00049`（:1829-1864）。它們不能當作第一 slice 的隱含依賴或被合併 dispatch。

唯一可評估重用的相鄰能力是 read-only `ORG-CALL-00064`，`fees.retrieve.by.dedication.period`：Data8 executor、ProductClient、CE 9.1 與 Embedded evidence 有部分完成，但 consumer 仍未遷移、CE 8.2/Dedicated evidence 未完成且仍 temporary legacy（:2399-2434）。未來它或可成為 server-authorized dedup/read projection 的候選；矩陣並未證明其 payment-write authorization、fixture、ledger、rollback 或 consumer cutover，因此在獨立驗證前不得採用。

## 最小受治理 first vertical slice

第一個 child 必須只處理 `payments.fee.update.after.payment`，並具備下列 release gate。

1. **Fresh descriptor 與 ledger：** 使用新 payment family/version、非空且從未使用的 nonce、不可變 descriptor digest、唯一 idempotency/correlation key、server-derived active/distinct owner binding，以及 fresh task-owned fee/booking/contact 的精確 ID。敏感 ID 僅存在該次受保護 ledger，絕不輸出至 console、TRX、診斷或 evidence。ledger 必須繫結 schema version、family、descriptor digest、owner/profile/generation、nonce、known IDs、pre/postimage digest、stage 與 idempotency key；拒絕跨 owner/profile/nonce replacement 與跳階。
2. **唯讀 preflight：** 先驗 descriptor/digest、nonce、empty single-writer ledger，再以 fixed, server-authorized exact-ID projection 證明 task marker、owner、fee 預影像、booking/payment transition 與 dedup 狀態。不得掃描、猜選、補修或產生 baseline；duplicate、paging、invalid owner、unavailable、authorization failure 或 unknown state 都是 no-go。若使用 `ORG-CALL-00064`，仍須證明其 server authorization 適用本 payment slice。
3. **單一 allowlisted dispatch：** 只接受 exact fee ID 與固定、bounded 的付款成功欄位集合；不得接受 generic `Entity`、field map、FetchXML、owner、booking/contact ID、profile、organization、endpoint 或 credential 作為 caller authority。dispatch 至多一次；exception、cancellation 或 timeout-after-dispatch 必須先 exact read-back/reconcile，永不 retry。local decision 同時必須符合 complete / succeeded / no matching processed order / awaiting payment。
4. **精確 read-back、reconcile 與 cleanup：** 用 ledger known fee ID 做固定 typed projection，與預先定義的 postimage 比對；不得靠名稱或全域搜尋確認。reconcile 僅讀 known IDs/key，無法判定即終態 no-go 並禁止 replay。cleanup 必須先還原 fee preimage 並 exact read-back，再刪除本 cycle 明確建立的 fee（若 preparation lane 建立），最後反向清除 fresh dependent graph，逐步證明 absence/baseline。任何 partial state、mismatch、unknown effect 或 cleanup failure 都保留 ledger 以供受控人工 reconciliation，不能自行刪 ledger 或宣稱成功。
5. **資源與隔離：** 所有 runtime/client/lease/logger/stream/process/temp directory/cancellation registration 都有單一 executor owner，依逆序 `finally` / `await using` 釋放。timeout、cancellation、fault 或 transport uncertainty 的 client 不得回池。不得保留 `HttpContext`、session、principal、payment observation、CRM entity 或 user/profile-specific mutable state 至 static、singleton、cache、queue 或背景工作。這是跨使用者隔離與資源生命週期的 release blocker（`.trellis/spec/backend/cross-user-isolation-and-performance.md:1-8`）。

fee create 若為必要的 fresh-fixture preparation，必須被建模為明確、獨立且可 cleanup 的 preparation lane；不可與本次 fee-update dispatch 混成一個無法判定的 transaction。owner assignment、booking completion、card profile 和 notification 同理；notification 尤其需獨立的安全 outbox/idempotency/retention 設計。

## Repository 無法證明的外部條件與後續 no-go

本次未執行 CE，故 repository 無法證明下列條件已成立：外部核准建立 payment fresh fixture/nonce/ledger 與進行一次 CE cycle；目標組織的 fee、booking、owner fields、狀態轉換、權限與預後影像；Data8/CE 9.1 profile、service identity、network 與 lease/disposal 行為；callback observation 到 server-authorized payment fee/booking 的安全映射；provider 重複 callback 與 timeout-after-provider/CRM-dispatch 的實際語義；`ORG-CALL-00064` 在 payment scope 的授權適用性；rollout/rollback owner、feature gate、其他 host/version readiness；以及 payment family 的 A/B concurrent isolation、資源基線回歸與 soak/profiling evidence。

在任一條件未獲證明時，結果必須是 no-go，而非 legacy fallback、雙寫、猜測性 repair 或重播。後續實作前應以新的 task-owned payment descriptor/ledger/provisioner 與 RED/GREEN tests 證明：fresh success、already processed、failed/pending/unknown/incomplete、stale ledger、descriptor mismatch、timeout-after-dispatch、read-back mismatch、cleanup uncertainty，以及 A/B interleaving 後所有資源回到宣告基線。
