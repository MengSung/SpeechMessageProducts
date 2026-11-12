# P4-05 回呼端點由 LineWebhook 承載

## 目標

依 04A §5 與 D4-8，在 `SpeechMessage.Payments.AspNetCore` 建立 QPay ATM／信用卡回呼端點和共用處理管線，並由 LineWebhook 掛載。端點必須先向永豐查證付款狀態，再由產品掛勾查找訂單及通過三道驗證；本包不新增產品掛勾實作，也不回寫收費單。

## 需求

- `POST /api/QPayAtm/QPayBackendUrl` 接受 JSON `ShopNo`、`PayToken`；`POST /api/QPayCard/QPayReturnUrl` 接受表單 `ShopNo`、`PayToken`。
- 只有 `Payment:Callbacks:Enabled=true` 才映射兩個指定 POST 路徑；預設停用。保留既有行為，不搬移舊 ATM GET 路徑。
- 每個回呼內文最多 64 KiB；超限回 413，且不得解析、查詢供應商或呼叫產品掛勾。
- 只依伺服器設定的 `Credentials:ShopNo` 尋找唯一永豐 profile。沒有或多個相符 profile、callback 解析失敗時回 400；不得回退預設 profile。
- 先解析 callback，再以 `PayToken` 向永豐查詢付款狀態並驗證回應簽章。供應商連線失敗回 503，以允許重送。
- 沒有註冊 `IPaymentCallbackOrderLookup` 產品掛勾時回 503；找不到訂單時回 200 且不更新。
- 訂單 profile 必須與 callback 使用的 profile 相同；已結清訂單回 200 且不更新。付款金額須存在且大於零，幣別須與訂單一致，付款訂單編號須屬於該訂單；驗證拒絕回 409，不呼叫付款後流程。
- 全部驗證通過後才呼叫既有 `PaymentPostPaymentWorkflow`。ATM 成功回應為 `{"Status":"S"}`，信用卡成功回應為純文字。
- 使用 LineWebhook 共用 `ExceptionDiagnostics` 傳遞真實例外；診斷與 HTTP 回應不得包含 PayToken、金鑰、個資或原始回呼本文。依 AGENTS.md，預期的 409 業務拒絕不得觸發 LINE 告警。
- 維持既有 Webhook 主機測試行為；ChurchReport、其設定檔、收費單回寫及實際產品掛勾均不在本包範圍。

## 驗收條件

- 以下六個測試名稱必須逐字存在並通過：
  - `Callback_ShopNoMismatch_Rejected`
  - `Callback_Duplicate_ProcessedOnce`
  - `Callback_AmountOrCurrencyMismatch_Rejected`
  - `Callback_NoProductHandler_Returns503`
  - `Callbacks_Disabled_NotMapped`
  - `Callback_BodyTooLarge_413`
- 依 04A 指令執行 `dotnet test SpeechMessageProducts.LineWebhook.Tests -c Release --filter "FullyQualifiedName~PaymentCallback"`；並確認既有 Webhook 主機測試仍通過。
- 僅修改 04A 指定的產品檔與測試檔，不新增 `.csproj`，不擴充 `tools\LegacyGolden`，不修改 3.5 計畫正文。
- 完成回報登錄 P4-PROP-001、P4-PROP-002；不得在本包實作提案內容。
