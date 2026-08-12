# 定期定額付款回傳呼叫鏈研究（2026-08-13）

## 證據來源

- `SpeechMessageProducts.ChurchReport/Payments/DonationPaymentReturnWorkflow.cs`
- `SpeechMessageProducts.ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `SpeechMessage.Dynamics.ProductClient/FeeReads/IPackage01FeeReadClient.cs`
- `SpeechMessage.Dynamics.ProductClient/FeeReads/Package01FeeReadClient.cs`
- `.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json`
- `.trellis/tasks/archive/2026-08/08-12-p7-2-continuation-release-candidate/d-h-callsite-map-2026-08-12.md`

## 現有呼叫鏈

`PaymentReturnController` 將 provider 結果交給 `DonationPaymentReturnWorkflow`；它建立
`DonationPaymentWorkflowResult` 後由 `DonationPaymentProductWorkflowDispatcher` 以產品分類派送。
recurring dedication branch 建立 `RecurringDonationPaymentProcessor`，並呼叫
`HandlePaymentReturn`。

該 processor 的 side effect 順序如下：

1. `RetrieveEntity("new_dedication_booking", ProductEntityId)` 讀取認獻單。
2. `RetrieveFeeByFetchXml(bookingName, bookingId, "001")` 做第 001 期 dedup 判斷。
3. 讀取 contact；若收到 card token 且舊字串判定不同，更新 contact 的 `new_visa_info`。
4. `CreateFee` 建立 `new_fee`，讀回新 fee，然後以 contact owner 指派 fee owner。
5. 更新 booking paid period、status、explain。
6. 以 LINE 通知成功或失敗，並回傳 Razor view。

這些操作沒有共同可證明的 transaction/read-back/cleanup owner。尤其第 4 步成功、第 5 步 timeout
時，舊流程不能證明 fee 或 booking 是否已提交；重播 callback 會有重複與不一致風險。

## 已存在的 typed read 與其限制

`IPackage01FeeReadClient.RetrieveFeesByDedicationPeriodAsync` 已固定使用
`fees.retrieve.by.dedication.period`，輸入為 profile、workload、booking ID、paid period 與可選名稱。
在 authoritative matrix 中，`ORG-CALL-00064` 的 registry/executor/ProductClient 已 implemented，
CE 9.1/Embedded evidence 是 succeeded，但 consumer 仍是 not-migrated，Dedicated evidence pending。

因為讀取結果緊接著決定 fee create、owner assignment、booking update 與通知，它不能獨立接到 legacy
processor；這會形成 read-new/write-legacy 的未受治理雙路徑。故本 child 僅在 local-only layer 保留
對 dedup/read-back 語意的需求，沒有將 typed read 接入 consumer。

## 外部模型狀態

2026-08-13 以 project self-healing CCG runner 啟動 architect 雙模型分析。runner 在 45 秒上限內未產生
可用 Gemini/Claude 結果，只留下 health/prompt artifact；依任務授權，此次結果標記為「雙模型未完成」。
本 child 改採本機 code/archived evidence 進行設計與驗證，不將其描述為完整雙模型分析。
