# P4-02 金額改用 Money

## 目標

依 04A §5 P4-02，讓 `SpeechMessage.Payments` 參考 `SpeechMessage.Localization`，以 `Money` 作為付款核心公開金額型別；保留舊 decimal／Currency 成員為 `[Obsolete]` 轉接，讓既有 ChurchReport 呼叫端在本包不必修改。三個 provider 明確宣告支援幣別，Gateway 對不支援的幣別回傳請求錯誤且不得送出 HTTP。

## 範圍與限制

- 僅修改 04A §5 P4-02 列出的架構測試、付款核心模型／介面／Gateway／三個 provider 與 mapper、Workflows mapper、ASP.NET Core request factory，以及新增的 `SpeechMessage.Payments.Tests\Models\PaymentMoneyTests.cs`。
- `SpeechMessage.Payments` 架構層級設為 8；Localization 維持層級 5；其他相依邊界不放寬。
- `PaymentCreateRequest.Total`、`PaymentLineItem.Price` 使用 `Money`；`PaymentStatusResult.PaidAmount`、`PaymentCallbackResult.PaidAmount` 使用 `Money?`。
- `IPaymentProvider.SupportedCurrencies` 由永豐、MyPay、台新各自宣告為 `TWD`。不支援幣別不得自動換算。
- ChurchReport 與 `ChurchReport.MemberInfo.Tests` 不修改；ChurchReport 只透過舊成員轉接相容。記錄本包新增的 obsolete 警告數量。
- 不新增 `.csproj`，不更動金流設定或既有秘密。

## 驗收條件

- [ ] `Payments_ToLocalization_IsAllowed` 與 `Localization_ToPayments_IsRejected` 同名測試存在且通過。
- [ ] `Money_TwdRoundTrip_NoDecimalLoss` 證明 `1234.56 TWD` 經永豐最小單位 `123456` 往返後仍精確相等。
- [ ] `CreateOrder_UnsupportedCurrency_Rejected` 證明永豐設定檔收到 `10 USD` 時回傳 `Status=Failed`、`Error.Kind=RequestInvalid`，假 HTTP handler 呼叫次數為 0。
- [ ] 執行 `dotnet test SpeechMessage.Payments.Tests -c Release` 與 `dotnet test SpeechMessage.Hosting.Tests -c Release --filter "FullyQualifiedName~Architecture"`；指定四個測試同名存在且通過。
- [ ] 全方案建置 0 錯誤；`SpeechMessage.Payments`、`.Workflows`、`.AspNetCore` 不使用新增的 obsolete 成員。
- [ ] ChurchReport 與 `ChurchReport.MemberInfo.Tests` 無變更；提交後固定 guard §8.1 全部通過，工作包一頁回報及進度 DATA 完成。
- [ ] 依 00 §7.1 五項驗收後推送 P4 分支並以 `git ls-remote` 核對遠端 SHA。
