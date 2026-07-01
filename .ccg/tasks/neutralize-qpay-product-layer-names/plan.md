# 實作計畫

1. 盤點目前 ChurchReport / tests 中 `QPay`, `Qpay`, `qpay` 命名殘留，分類為 provider protocol、legacy route、產品層待清理、測試/文件誤判。
2. 先調整 guard / workflow tests，讓測試描述新的命名邊界：產品層不再期待 QPay alias，且共用付款結果 View path 應為 `PaymentReturn`。
3. 將 ChurchReport 產品層共用結果頁徑由 `~/Views/QPayCard/PaymentResult.cshtml` 改為 `~/Views/PaymentReturn/PaymentResult.cshtml`。
4. 將奉獻付款頁前端命名改成中性 `DonationPayment`：CSS 檔名、class、form action 以中性名稱為主；如舊 URL 必須存在，只保留 route 相容，不保留 QPay alias 類別。
5. 將 `DonationPaymentProcessor` 中共用設定欄位 `QPAY_ORGANIZATION` / `QPayOrganization` 改為 `PAYMENT_ORGANIZATION` / `PaymentOrganization`，讀取 `Payment:Organization` 並 fallback 到舊設定鍵。
6. 清理 `ChurchReport.csproj` 中已不存在或非必要的 QPay include/remove 紀錄。
7. 執行 build、payment core tests、ChurchReport payment-related tests，以及 QPay boundary search；修正合理範圍內的回歸。
