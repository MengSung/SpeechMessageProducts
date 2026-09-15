# 需求

- 參考桌面 DonationPaymentView.cshtml，改善 SpeechMessageProducts.ChurchReport 的奉獻操作頁面。
- 支援最多七筆多類別奉獻，每筆包含 Category、Amount、Others。
- 定期定額限制為單一類別。
- 沿用既有付款 Callback 與 CRM 收費單更新，不複製秘密或跨 Session 狀態。
- 付款成功後同步更新收費單實收金額與付款狀態；重複 Callback 必須冪等。
