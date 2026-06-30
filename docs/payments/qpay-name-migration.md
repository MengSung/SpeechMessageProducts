# QPay Name Migration

## 決策

ChurchReport 的產品流程類別不應以 QPay 作為主要名稱，除非該程式碼明確屬於永豐 Sinopac/QPay provider protocol、既有公開路由，或暫時性的相容包裝。

這次命名整理只處理 ChurchReport 產品層的 UI 狀態、付款建立對接、付款回傳、CRM 更新與 LINE 通知流程；不把 ASP.NET Controller、CRM、LINE 或 ChurchReport model 移入 `SpeechMessage.Payments`。

## 新舊名稱對照

| 舊名稱 | 新名稱 | 狀態 |
| --- | --- | --- |
| QPayProcessor | DonationPaymentProcessor | ChurchReport 奉獻付款流程 processor |
| QpayManager | DonationPaymentManager | UI/payment state manager |
| QPayCreatePaymentGatewayAdapter | DonationPaymentCreateGatewayAdapter | ChurchReport 建立付款 adapter |
| QPayReturnWorkflow | DonationPaymentReturnWorkflow | 付款回傳 workflow |
| QPayWorkflowPaymentResult | DonationPaymentWorkflowResult | 產品 workflow DTO |
| QPayCardController | PaymentReturnController | provider 回傳 endpoint |
| QPayFeeProcessor | DonationFeePaymentProcessor | 收費單付款完成後流程 |
| QPayDedicationBookingProcessor | RecurringDonationPaymentProcessor | 定期定額認獻付款完成後流程 |
| QPayPaymentResultHelper | DonationPaymentResultHelper | 付款結果判斷 helper |
| QPayPaymentDebugLogger | DonationPaymentDebugLogger | 付款流程除錯記錄 |

## 仍可保留 QPay 的位置

- 既有公開路由與 view，例如 `/QPayLogin`、`/Home/QPayView/{LineId}`、`/Dedication/QPayView/{LineId}`、`~/Views/QPayCard/PaymentResult.cshtml`。
- `QPay*` 舊類別相容 wrapper，且 wrapper 只能轉交到新中性類別。
- 永豐 Sinopac/QPay provider protocol 相關資料欄位或舊 CRM 欄位，例如 `new_q_paid_card_order_no`。
- 舊建單相容模型，例如 `QPayCreatePaymentInput`，等下一階段再改名。

## 延後項目

路由與 view 名稱可能被使用者書籤、前端連結或金流後台 callback 設定使用，因此不在本階段改名。若要移除這些公開 URL 的 QPay 字樣，應另寫 route migration plan，並包含 redirect、provider callback 設定、前端連結與回歸測試。