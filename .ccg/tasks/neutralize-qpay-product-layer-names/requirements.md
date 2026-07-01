# 清理 ChurchReport 產品層 QPay 命名

## 目標

ChurchReport 產品層不得讓高鉅 MyPay、台新 TSPG、共通付款流程看起來依賴永豐 QPay。除了真正的永豐/Sinopac provider protocol、必要的歷史 URL 相容入口，以及測試中明確允許的 legacy route 字串外，其餘產品層命名應改為中性名稱。

## 邊界

- `SpeechMessage.Payments.Providers.Sinopac` 可以保留 QPay 命名，因為那是永豐協定本身。
- `ChurchReport` 可以保留 MVC route template 中對外既有 URL 的相容入口，但 C# 類別、方法、View、CSS、共用 workflow 命名應改為中性。
- 不新增 `QPay alias` 類別或包裝器。
- 不把 CRM 更新、LINE 通知搬進 `SpeechMessage.Payments`；這些仍是 ChurchReport 產品流程，只透過中性 workflow/result 串接。
- 不改金流核心 provider dispatch 行為，避免影響永豐、高鉅、台新、LINE Pay 的既有付款流程。

## 驗收條件

- ChurchReport 共用付款結果頁使用 `PaymentReturn` 命名，不再使用 `QPayCard` View path。
- 奉獻付款頁使用 `DonationPayment` 命名，不再以 `QPayView.css` / `qpay-*` 作為主要產品層名稱。
- `DonationPaymentProcessor` 內部的共用付款組織設定改為中性 `PaymentOrganization`，必要時保留讀取舊 `QPAY_ORGANIZATION` 設定鍵作為設定相容。
- MyPay 與台新 TSPG 不呼叫 QPay provider toolkit/model/status 類別。
- guard test 的允許範圍清楚，不把 historical docs 或 `.ccg` 記錄誤判為產品層 runtime 污染。
- build 與相關 payment tests 通過，或明確列出既有非本次範圍失敗。
