# 顯示 ATM/匯款奉獻與輸入奉獻 LINE 發送結果

## 需求

使用者要求：ATM/匯款奉獻及輸入奉獻時，要把 LINE 訊息發送結果顯示給使用者，包括成功發送或發送失敗原因。

## 初步觀察

- LINE Messaging quota 已查到 limited 200 / usage 200，因此目前實際環境推播額度已用完。
- ATM/匯款付款後路徑目前在 DonationFeePaymentProcessor 直接呼叫 PushUtility.SendMessage，但沒有把發送成功/失敗狀態寫回 ViewBag。
- 輸入奉獻路徑走 DonationKeyInDedicationService.SaveAsync/UpdateAsync，目前未看到對奉獻者的 LINE 發送結果回傳。

## 驗收標準

- ATM/匯款奉獻結果頁能顯示 LINE 發送成功。
- ATM/匯款奉獻結果頁能顯示 LINE 發送失敗原因。
- 輸入奉獻 AJAX/JSON 回應能顯示 LINE 發送成功或失敗原因。
- 既有 CRM 更新流程不因 LINE 發送失敗而整筆中斷。
