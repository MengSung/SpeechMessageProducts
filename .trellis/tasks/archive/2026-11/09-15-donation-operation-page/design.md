# 技術設計

頁面只負責收集與呈現奉獻列，提交仍使用既有 `DonationPaymentFormModel` 與 `SaveDonationPaymentDedication`。前端以 request-owned 陣列維持列順序，送出前將每列 Category、Amount、Others 序列化為目前後端可接受的欄位；不把 Session 或 CRM 物件暴露給瀏覽器。

付款建立沿用現有 `SpeechMessage.Payments` 與 ChurchReport adapter。PaymentReturn 由既有 workflow 依交易冪等鍵查找收費單，成功時原子更新實收金額、付款狀態與付款日期；本次頁面修改不直接繞過該 workflow 寫 CRM。

若現有模型尚未具備多列欄位，先在測試中固定正常化與拒絕規則，再增加最小 DTO/normalizer，並在 Controller 入口驗證 scope、Session 與列數。
