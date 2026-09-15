已完成唯讀分析，報告已寫入 `.ccg/tasks/donation-operation-page/analysis.md`。核心結論摘要如下：

## 現有資料流
- **表單/建單**：`DedicationController.SaveDonationPaymentDedication` → `DonationPaymentManager.SaveDonationPaymentDedicationAsync` → `DonationPaymentProcessor.CreateFeeAsync`。`DonationPaymentFormModel` 是**單列**模型（Category/Amount/Others 皆為純量），`CreateFeeAsync` 依 PayWay 路由到信用卡/定期定額/ATM/LinePay/行動支付分支，每次呼叫只建立**一筆** `new_fee`。
- **回呼**：`PaymentReturnController.Return` → `IPaymentGateway`（provider 協定）→ `IDonationPaymentReturnWorkflow` → `DonationPaymentProductWorkflowDispatcher` → **實際生效**的是 `DonationFeePaymentProcessor.HandlePaymentReturn`（靠 `new_payment_records` 字串比對 + `new_pay_status` 做冪等）。
- **Session isolation** 已相當完整（LINE / 網頁登入互斥、`TryGetVerifiedLineUserId` 權威來源、`ClearLineDonationState` 逐欄清空），但目前只覆蓋單一欄位，尚未涵蓋未來的多列陣列。

## 關鍵缺口
1. 沒有多列（最多七列）資料結構與建單契約（一次送出對應 N 筆收費單，還是 1 筆+明細子表，尚未決定）。
2. 定期定額單列限制目前只在前端 JS，後端無驗證。
3. 冪等鍵綁在「單一收費單 + 單一 OrderNo」，不相容多列多筆收費單场景。

## 重大風險（Critical）
- **ATM 回呼被 `DonationFeePaymentProcessor` 硬編碼為「信用卡」文案與 CRM `new_pay_way` 值**——若移植後 ATM 走同一路徑，收費單會被系統性誤標。
- **`PaymentPostPaymentWorkflow`/`ChurchReportPaymentRecordUpdater` 這條共用管線已在 DI 註冊但因 dispatcher 手動 `new` 建構子而從未被觸發**，且它完全沒有冪等防護——是個「看似安全、實則地雷」的死碼路徑，未來重構若不慎接上會立刻造成重複入帳與重複 LINE 通知。
- 多列建單一旦與單一 `OrderNo` 冪等機制混用，會有「部分收費單漏更新」或「全部重複入帳」的雙向風險。

報告內含完整的最小修改檔案清單、9 項建議測試（單元/隔離/端對端）與 3 個需要與需求方確認的架構決策點。全程未修改任何程式碼。

---
SESSION_ID: a0901512-6a37-420f-8427-eed4a8d8c29f
