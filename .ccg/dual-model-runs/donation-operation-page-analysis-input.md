# 分析任務

請唯讀分析目前 repository 的 `SpeechMessageProducts.ChurchReport` 奉獻頁、DonationPaymentFormModel、DonationPaymentManager、DedicationController、PaymentReturnController、DonationFeePaymentProcessor 與 SpeechMessage.Payments 整合。

目標是將參考頁的多類別奉獻操作體驗移植至現有專案，同時保留最多七列、定期定額單列、信用卡與 ATM、Session isolation、Callback 冪等，以及成功付款後 CRM 收費單實收金額與付款狀態同步。

請輸出：現有資料流、缺少的契約、最小修改檔案、主要安全/隔離/資源生命週期風險、建議測試。不得輸出 appsettings 的任何秘密值。
