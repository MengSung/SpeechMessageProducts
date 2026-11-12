# P4-05 實作順序

依 04A §5 固定範圍執行；每一步先看紅燈，再完成實作後看綠燈。不得擴增檔案清單或測試名稱。

1. 更新父任務子任務連結至本任務實際目錄，核對 P4-02 父任務說明仍符合 04A。
2. 以使用者核准的規劃建立本任務執行狀態：`python ./.trellis/scripts/task.py start .trellis/tasks/11-13-p4-05-payment-callback`。
3. 先新增 `SpeechMessageProducts.LineWebhook.Tests\PaymentCallbackHostTests.cs`，逐字建立六個案例：`Callback_ShopNoMismatch_Rejected`、`Callback_Duplicate_ProcessedOnce`、`Callback_AmountOrCurrencyMismatch_Rejected`、`Callback_NoProductHandler_Returns503`、`Callbacks_Disabled_NotMapped`、`Callback_BodyTooLarge_413`；以要求的測試指令確認紅燈。
4. 新增 workflow lookup contract `SpeechMessage.Payments.Workflows\Callbacks\IPaymentCallbackOrderLookup.cs`，使用 04A §4 的 `FindAsync` 簽章及 `PaymentCallbackOrder` 欄位。
5. 新增 ASP.NET Core processor 與 endpoint；實作唯一 ShopNo profile 選擇、ParseCallback→供應商查詢驗簽→lookup→三道驗證→既有付款後 workflow 順序，以及 64 KiB 有界本文和不洩漏的錯誤處理。
6. 修改 `PaymentAspNetCoreServiceCollectionExtensions.cs` 註冊共用 scoped processor；修改 LineWebhook `Program.cs` 加入 `AddSpeechMessagePaymentAspNetCore()` 與 `app.MapSpeechMessagePaymentCallbacks()`，不註冊產品掛勾。
7. 執行 `dotnet test SpeechMessageProducts.LineWebhook.Tests -c Release --filter "FullyQualifiedName~PaymentCallback"`，確認六個指定測試均存在並通過；再跑既有 LineWebhook 主機測試確認相容性。
8. 核對所有新增／修改 C# 的繁體中文檔首整體說明、成員 XML 註解、UTF-8 無 BOM、CRLF 與檔尾 CRLF；確認不曾記錄或提交秘密，也未修改 04A 以外檔案。
9. 提交程式變更，於固定 `C:\smv\guard` 執行 00 §8.1 全部檢查；原始輸出與 TRX 留在本機 evidence，回報只記檔名與 SHA-256。
10. 撰寫一頁 `wp-reports/P4-05.md`，明示 04A 完成段落寫「五個測試」與步驟列出的六個具名測試不一致，本包依六個名稱驗收；登錄 P4-PROP-001、P4-PROP-002，更新進度頁 DATA（台北時間），標記驗收並封存 Trellis 子任務。
11. 對只含紀錄的提交執行 00 §8.3 三項檢查；驗收完成後推送目前 P4 分支並以 `git ls-remote` 核對遠端 SHA，不推 main、不開 PR。

## 範圍停損

- 不新增產品回寫 handler、不更新任何收費單、不修改 ChurchReport。
- 不搬移舊 ATM GET、不改 provider 協定、不引入記憶體去重或跨 request 狀態。
- 不部署 LineWebhookP309、不啟動完整產品服務、不連 D365 或永豐沙箱、不接觸正式商店與正式 LINE Channel。
- P4-05 只做指定離線回呼測試；階段 P4-06 與階段審查留待後續包。
