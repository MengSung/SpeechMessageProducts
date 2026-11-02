# 實作計畫

1. 先建立多列輸入正常化與驗證的失敗測試，涵蓋 1、2、7 列、重複類別、0/負數、超過 7 列與定期定額多類別。
2. 檢查現有 `DonationPaymentFormModel`、manager 與 payment mapper；若已支援列資料，僅調整頁面 binding。
3. 以範例頁面為視覺與互動參考，重寫或局部替換奉獻輸入區，保留既有付款方式、信用卡、ATM、定期定額與登入隔離行為。
4. 補強成功付款同步測試，確認 CRM 收費單實收與狀態只在成功、且冪等條件成立時更新。
5. 執行 `dotnet test`、`dotnet build`、`dotnet publish` 與 `git diff --check`。
6. 以 UTF-8 無 BOM、CRLF、最終 CRLF 檢查所有修改的 `.cs`/`.cshtml`。
