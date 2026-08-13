# P7.2 受控付款回傳家族架構分析（2026-08-14）

## 已檢查的權威來源

- `SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalDecision.cs`
- `SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalPlanBuilder.cs`
- `SpeechMessage.Dynamics.Tests/P72DonationPaymentLocalDecisionTests.cs`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedger.cs`
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs`
- `.trellis/tasks/archive/2026-08/08-13-p72-dedication-payment-return-write-boundary/`
- `.trellis/tasks/archive/2026-08/08-12-p7-2-continuation-release-candidate/`
- `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json`

## 確認結果

1. **不可復用歷史 Slice C control plane。**
   `P72FreshSliceCFixture*` 的 nonce、ledger、descriptor、environment variables、fixture graph 與
   evidence 都服務 list-management family；歷史 cycle 的結果是 `write-not-committed` no-go 並已 cleanup。
   新 payment family 即使只復用其中的 identifier 或 fixture baseline，也會破壞 fresh-family/no-replay
   不變量。
2. **legacy payment processor 不是 writer boundary。**
   `RecurringDonationPaymentProcessor.HandlePaymentReturn` 同時執行 dedup read、contact card update、
   fee create、fee-owner assignment、booking update 與 notification，沒有由同一個 ledger owner 證明的
   exact read-back、reconcile、reverse cleanup 或 timeout-after-dispatch 規則。因此不可直接接線、包裝或
   將它視為一個 transaction。
3. **既有 P72 payment types 只能當 local-only predecessor。**
   `P72DonationPaymentLocalDecision` 和 `P72DonationPaymentLocalPlanBuilder` 已針對 incomplete、
   unknown、pending、already processed 與 failure fail closed，且明確維持
   `CeDispatchAllowed=false` / `ProductConsumerAllowed=false`。它們不帶 CRM ID、Owner、credential、
   endpoint 或 executor，適合與新 admission contract 相鄰，但不能取代 descriptor/ledger/preflight。
4. **第一個 slice 應只涵蓋 fee update。**
   `payments.fee.update.after.payment` 可以做為第一個 future governed writer；fee create、owner assign、
   booking completion 及 notification 必須各自擁有後續 writer slice，否則會將 partial completion
   偽裝成成功 transaction。
5. **P7.4/P7.5/P8 不在本 child 範圍。**
   這是 local-only payment family planning，不啟用 consumer、feature flag 或 traffic，不進行 ToolUtility
   removal、Central Gateway deployment 或 CE 8.2/Official Worker 操作。

## 新 family 的最低 future executor 契約

- 新 family name、new nonce、descriptor digest 與單一 immutable ledger binding；不得接受 caller 或
  environment 提供的歷史 binding。
- preflight 是零 mutation、fixed de-identified category；除了 `go` 外，一律不 provision 或 dispatch。
- descriptor 僅含本 task fresh booking/contact/fee 的 secured exact IDs、marker、preimage/postimage
  digest、server-derived owner binding 與一個明確 mutation allowlist。
- 第一个 slice allowlist 只允許 fee update；其餘 mutation 沒有授權。
- dispatch 計數恰好為一次；timeout、ambiguous 或 partial 永遠 `ProhibitsReplay=true`。
- read-back 是 exact typed scalar projection；mismatch、unavailable 或 unknown effect 都為 no-go。
- cleanup 只依 ledger known keys 反向執行；cleanup failure/uncertainty 不猜測，不刪除其他資料，且為
  terminal no-go。

## 需要 RED/GREEN 的最小測試

- fresh binding + non-empty nonce + descriptor complete + empty ledger + preflight `go` 才可 provision；
- historical family、缺 nonce、descriptor incomplete、non-empty/stale ledger、任何 preflight no-go 都拒絕；
- operation executed 的 timeout、ambiguous、partial、read-back mismatch、cleanup uncertain 不得 replay；
- already processed、failed、pending、unknown、incomplete observation 不得產生 dispatch plan；
- A/B interleaving 不可共用 mutable admission state；
- success path 仍維持 `CeDispatchAllowed=false` 與 `ProductConsumerAllowed=false`。

## 外部分析狀態

以 `Start-CcgDualModelRun.ps1` 嘗試 Gemini + Claude architecture review，最多等待 45 秒。Gemini 產出
與上述結論一致的可用意見；Claude 在時間上限內未完成，runner 未產生可用 summary。因此本次記錄為
**雙模型未完成**，採本機審計與可用 Gemini 結論繼續，不把它宣稱為完整雙模型分析。
