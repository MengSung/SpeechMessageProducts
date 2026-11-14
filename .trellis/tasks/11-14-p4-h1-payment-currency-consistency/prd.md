# P4 審查 H-1：付款總額與明細幣別一致性

## Goal

修補 MyPay 建單邊界，拒絕總額與任一明細幣別不一致、default Money、空明細或明細合計不符的請求，並以測試證明拒絕前不發生 HTTP 呼叫。完成後跑指定測試、文件／編碼檢查與適用守門，更新 P4 審查回報與 P5 父任務狀態。

## Requirements

- 修改範圍僅限 `SpeechMessage.Payments\Gateway\PaymentGateway.cs` 與
  `SpeechMessage.Payments.Tests\Models\PaymentMoneyTests.cs`。
- `PaymentGateway.CreatePaymentAsync` 在驗證總額 `Money.Currency` 有效且為
  provider 支援幣別後，於呼叫 provider 前逐筆讀取 `request.Items` 的
  `Price.Currency`。
- 任一明細幣別無效（包含 default `Money`）或不等於總額幣別時，必須回傳
  `PaymentStatus.Failed`／`PaymentErrorKind.RequestInvalid`，且不得呼叫 provider
  或發出 HTTP。
- 沒有明細時保留現有 MyPay fallback 行為；本修補不檢查明細合計是否等於總額。
- 新增兩個具名測試 `CreateOrder_LineItemCurrencyMismatch_RejectedBeforeHttp` 與
  `CreateOrder_DefaultMoneyLineItem_RejectedBeforeHttp`，各自以永豐與 MyPay
  provider 的計數型 HTTP handler 證明拒絕發生在傳輸前。
- 測試不得讀取秘密、正式組織、正式 LINE channel 或外部服務；測試建立的
  `HttpClient`／handler 必須由案例確定性釋放。

## Acceptance Criteria

- [ ] 兩個具名測試存在，且每個測試同時覆蓋永豐與 MyPay，全部通過。
- [ ] mismatch 與 default `Money` 均回傳 `Failed`／`RequestInvalid`，兩家
  provider 的 HTTP handler 呼叫次數均為 0。
- [ ] `SpeechMessage.Payments.Tests` 全綠，既有無明細行為未被改變。
- [ ] 變更的 `.cs` 檔維持繁中檔首說明、XML 文件、UTF-8 無 BOM、CRLF、
  final CRLF；文件與適用品質檢查通過。
- [ ] 產生一頁 H-1 回報並更新 P5 父任務／階段 4 進度紀錄，記錄實際日期時間、
  測試結果與提交 SHA；不建立 P5-01 以前的任何模組部署或實機操作。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
