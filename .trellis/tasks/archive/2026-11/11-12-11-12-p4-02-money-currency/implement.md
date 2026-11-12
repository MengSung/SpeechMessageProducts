# P4-02 執行清單

依 04A §5 P4-02 順序執行，不修改 ChurchReport 或 `ChurchReport.MemberInfo.Tests`。

1. 在 `SpeechMessage.Hosting.Tests\Architecture\ArchitectureRuleTests.cs` 先新增 `Payments_ToLocalization_IsAllowed`、`Localization_ToPayments_IsRejected`，執行確認紅燈。
2. 在 `ArchitectureRules.cs` 將 `SpeechMessage.Payments` 從層級 0 群組移出，單獨列為層級 8；執行兩個架構測試確認轉綠。
3. 新增 `SpeechMessage.Payments.Tests\Models\PaymentMoneyTests.cs` 中的 `Money_TwdRoundTrip_NoDecimalLoss`，以 `1234.56m` → 永豐最小單位 `123456` → `Money` 驗證無 decimal 損失。
4. 更新 `SpeechMessage.Payments.csproj` 參考 `SpeechMessage.Localization`；在四個指定模型新增 `Total`、`Price` 或 `PaidAmount` Money 成員，舊 decimal／Currency 成員保留為 `[Obsolete]` 轉接。
5. 新增 `IPaymentProvider.SupportedCurrencies`，永豐、MyPay、台新宣告 `TWD`；調整 provider、mapper、Gateway、Workflows mapper 與 request factory 使用新成員。
6. 新增 `CreateOrder_UnsupportedCurrency_Rejected`：永豐設定 profile 加 `10 USD` 必須得到 Failed／RequestInvalid，假 handler 呼叫次數為 0；確認先紅再實作 Gateway provider 能力檢查。
7. 執行 `dotnet test SpeechMessage.Payments.Tests -c Release` 與 `dotnet test SpeechMessage.Hosting.Tests -c Release --filter "FullyQualifiedName~Architecture"`；四個指定名稱必須存在且通過。
8. 檢查 ChurchReport 與 `ChurchReport.MemberInfo.Tests` 未變更、三個共用 Payments 專案無新增 obsolete 使用；記錄全方案建置中的新增 obsolete 警告數量。
9. 更新每個修改之 `.cs` 檔案的繁中檔首說明及必要 XML 文件；執行編碼位元組檢查、測試與全方案建置，檢視差異。
10. 以 `P4-02 金額改用 Money 並由 provider 宣告支援幣別` 提交程式；在固定 `C:\smv\guard` 執行完整 00 §8.1，保存本機 log 與 SHA-256。
11. 依 00 §7.1 完成一頁 `wp-reports\P4-02.md`、進度頁 DATA、驗收、推送 P4 分支並以 `git ls-remote` 核對 SHA；之後封存子任務。
