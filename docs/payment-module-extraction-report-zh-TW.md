# 金流模組抽離報告

日期：2026-06-27
分支：`payment-module-extraction`
工作目錄：`.worktrees/payment-module-extraction`

## 1. 摘要

本次金流模組抽離的核心目標，是把原本散落在 `ChurchReport` 裡的永豐 QPay、高鉅 MyPay、台新 TSPG provider protocol 程式碼，集中到一個獨立、可重用、無產品流程依賴的 class library：

```text
SpeechMessage.Payments
```

抽離後的架構遵守下列邊界：

```text
ChurchReport Controllers / workflows / CRM / LINE / views
    -> ChurchReport.Payments thin adapters
        -> SpeechMessage.Payments IPaymentGateway
            -> Providers: Sinopac, MyPay, Taishin
```

`SpeechMessage.Payments` 現在負責金流 provider 的協定細節，包括 request/response mapping、簽章、加密、解密、callback parsing、callback acknowledgement、狀態正規化、錯誤正規化與敏感資料遮蔽。

`ChurchReport` 則保留產品流程，包括 ASP.NET Controller、HTTP request binding、CRM fee 更新、LINE 通知、奉獻/收費分類、舊 QPay processor 相容層、付款成功/失敗頁面與轉址。

這樣切分後，未來其他產品可以引用 `SpeechMessage.Payments`，透過 JSON 設定選擇金流 provider，而不需要把 ChurchReport 的 Controller、CRM、LINE 或奉獻流程一起帶過去。

## 2. 本次抽離的設計原則

本次實作依照下列原則進行：

1. 金流核心只做金流，不做產品流程。
2. 通用核心不依賴 ASP.NET Controller、`HttpRequest`、CRM SDK、LINE SDK、ChurchReport 工具類別或資料庫。
3. Public contract 使用 provider-neutral DTO，不再讓 QPay 型別成為主要 API。
4. Provider-specific DTO、狀態碼、簽章、加解密與 callback parser 全部放在 `SpeechMessage.Payments.Providers.*`。
5. ChurchReport 只保留薄轉接層，用來把既有 Controller、CRM、LINE、View workflow 接回新的金流核心。
6. `LinePayCSharp` 不納入第一版抽離範圍，避免把不同生命週期的 Line Pay SDK 混進本次核心。
7. 第一版只涵蓋建立付款、查詢付款、callback parsing/verification、provider/profile selection、狀態與錯誤正規化；退款、請款、取消、對帳、分潤、後台 UI 不納入。

## 3. 新增專案

### 3.1 `SpeechMessage.Payments`

新增的通用金流核心專案：

```text
SpeechMessage.Payments/SpeechMessage.Payments.csproj
```

主要角色：

- 對 ChurchReport 與未來產品提供 provider-neutral `IPaymentGateway`。
- 管理多 profile 設定與 provider routing。
- 封裝永豐、高鉅、台新的 provider protocol。
- 統一回傳 `PaymentCreateResult`、`PaymentStatusResult`、`PaymentCallbackResult`。
- 對 provider raw data 做敏感資料遮蔽後才輸出 diagnostics/provider data。

此專案加入 `ChurchReport.sln`，且由 `ChurchReport/ChurchReport.csproj` 以 `ProjectReference` 引用。

### 3.2 `SpeechMessage.Payments.Tests`

新增的金流核心測試專案：

```text
SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj
```

主要測試：

- options/profile binding。
- gateway provider routing。
- public model contract 不洩漏 QPay/ASP.NET 型別。
- diagnostics sanitizer 不洩漏 secret。
- MyPay payload、callback、acknowledgement、direct merchant / agent form。
- Sinopac AES key、create/query/callback、ATM 虛擬帳號、hosted URL fail-closed。
- Taishin TSPG JSON/form callback、hash verification、create payload。

## 4. 通用核心公開 contract

核心公開入口為：

```text
SpeechMessage.Payments/Abstractions/IPaymentGateway.cs
```

主要方法：

```csharp
Task<PaymentCreateResult> CreatePaymentAsync(
    PaymentCreateRequest request,
    CancellationToken cancellationToken = default);

Task<PaymentStatusResult> QueryPaymentAsync(
    PaymentQueryRequest request,
    CancellationToken cancellationToken = default);

Task<PaymentCallbackResult> ParseCallbackAsync(
    PaymentCallbackRequest request,
    CancellationToken cancellationToken = default);
```

設計重點：

- `PaymentCreateRequest` 使用 `ProfileName`、`ProductOrderId`、`Amount`、`Currency`、`PaymentMethod`、`Callbacks`、`Customer`、`Items`、`Metadata` 等中性欄位。
- `PaymentQueryRequest` 使用 `ProviderOrderRef`，不使用 QPay 專屬 `PaymentToken` 命名。
- `PaymentCallbackRequest` 可承載 `HttpMethod`、`ContentType`、`RawBody`、`Query`、`Form`、`Headers`，但不依賴 ASP.NET `HttpRequest`。
- `PaymentCallbackResult` 回傳 `ProductOrderId`、`ProviderTransactionId`、`Amount`、`Currency`、`Acknowledgement`、`ProviderData`、`Diagnostics`、`Error`。
- `PaymentCallbackAcknowledgement` 用 `PaymentAckKind` 描述 provider 需要的回覆形狀，例如 plain text、JSON、redirect 或 none。

內部 provider 入口為：

```text
SpeechMessage.Payments/Abstractions/IPaymentProvider.cs
```

`IPaymentProvider` 是核心內部 provider 擴充點，ChurchReport 不直接依賴它；ChurchReport 只依賴 `IPaymentGateway`。

## 5. 設定模型與 provider 選擇

設定模型集中在：

```text
SpeechMessage.Payments/Configuration/PaymentOptions.cs
SpeechMessage.Payments/Configuration/PaymentMerchantProfile.cs
SpeechMessage.Payments/Configuration/OptionsPaymentProfileResolver.cs
SpeechMessage.Payments/Configuration/PaymentOptionsValidator.cs
SpeechMessage.Payments/Configuration/PaymentConfigurationException.cs
```

目前 `ChurchReport/appsettings.json` 使用：

```json
{
  "PAY_PROVIDER": "永豐金流",
  "Payment": {
    "DefaultProfile": "JesusTest",
    "Profiles": {
      "JesusTest": {
        "Provider": "Sinopac",
        "Environment": "Sandbox"
      },
      "MyPayProduction": {
        "Provider": "MyPay",
        "Environment": "Production"
      },
      "TaishinSandbox": {
        "Provider": "Taishin",
        "Environment": "Sandbox"
      }
    }
  }
}
```

`SpeechMessage.Payments` 只認得 `Payment:DefaultProfile` 與 `Payment:Profiles`。
ChurchReport 的舊設定鍵 `PAY_PROVIDER` 由下列 adapter 轉成 profile name：

```text
ChurchReport/Payments/ChurchReportPaymentProfileResolver.cs
```

目前 mapping：

```text
永豐金流 -> JesusTest
高鉅金流 -> MyPayProduction
台新金流 -> TaishinSandbox
```

這個 mapping 留在 ChurchReport，因為 `PAY_PROVIDER` 是 ChurchReport 的歷史設定格式，不應綁進通用金流核心。

## 6. Dependency Injection

核心 DI extension：

```text
SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs
```

它負責註冊：

- `PaymentOptions`
- `IPaymentProfileResolver`
- `IPaymentGateway`
- Sinopac provider
- MyPay provider
- Taishin provider
- DI-managed `HttpClient`

ChurchReport 在：

```text
ChurchReport/Startup.cs
```

註冊：

```csharp
services.AddSpeechMessagePayments(Configuration.GetSection("Payment"));
services.AddScoped<PaymentHttpRequestMapper>();
services.AddScoped<PaymentAcknowledgementResultMapper>();
services.AddScoped<ChurchReportPaymentProfileResolver>();
services.AddScoped<PaymentCreateRequestFactory>();
services.AddScoped<PaymentWorkflowResultMapper>();
services.AddScoped<IQPayReturnWorkflow, QPayReturnWorkflow>();
services.AddScoped<IQPayProductWorkflowDispatcher, QPayProductWorkflowDispatcher>();
services.AddScoped<QPayCreatePaymentGatewayAdapter>();
```

其中 `AddSpeechMessagePayments(...)` 是通用核心註冊；其他 `ChurchReport.Payments` 類別是 ChurchReport HTTP/產品流程 adapter。

## 7. Provider 實作

### 7.1 永豐 QPay / Sinopac

主要檔案：

```text
SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs
SpeechMessage.Payments/Providers/Sinopac/SinopacRequestMapper.cs
SpeechMessage.Payments/Providers/Sinopac/SinopacCrypto.cs
SpeechMessage.Payments/Providers/Sinopac/SinopacSigner.cs
SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs
SpeechMessage.Payments/Providers/Sinopac/SinopacStatusMapper.cs
SpeechMessage.Payments/Providers/Sinopac/SinopacModels.cs
```

責任：

- 建立 QPay order create payload。
- 建立 QPay order query payload。
- 取得 nonce。
- 依永豐 QPay 規則加密/簽章。
- 解析 QPay return callback。
- 將 QPay 狀態轉成 `Pending`、`Succeeded`、`Failed`、`Cancelled`、`Unknown`。
- 將銀行 HTTP failure 正規化成 `PaymentErrorKind.ProviderUnavailable`，並保留 route 與 sanitized response body。

重要相容點：

- AES key 由 `A1/A2` 與 `B1/B2` XOR 後的大寫 hex 字串組成。
- Sandbox `JesusTest` profile 的 legacy AES key regression test 期望值為：

```text
89C697BCC1C10908864428F5C58A068A
```

- 信用卡、行動支付、LINE hosted payment 若 provider 回成功但沒有付款頁 URL，會 fail closed，不讓舊前端跳回原奉獻頁。
- ATM 建立付款若成功但沒有虛擬帳號，也會 fail closed。
- `atm_pay_no`、`web_atm_url`、`otp_url` 會放入 sanitized provider data，讓 ChurchReport 能顯示 ATM/轉帳付款資訊。
- ATM 虛擬帳號不會被 sanitizer 當成信用卡號遮蔽。

### 7.2 高鉅 MyPay

主要檔案：

```text
SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs
SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs
SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs
SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs
SpeechMessage.Payments/Providers/MyPay/MyPayStatusMapper.cs
SpeechMessage.Payments/Providers/MyPay/MyPayModels.cs
```

責任：

- 將 `PaymentCreateRequest` 轉成 MyPay create payload。
- 組 MyPay 外層 form 欄位。
- 處理 direct merchant 與 agent/reseller 兩種 form contract。
- 解析 MyPay callback。
- 將 MyPay `prc` 狀態碼轉成 normalized status。
- 回傳 MyPay callback 所需 plain text acknowledgement。

重要相容點：

- Direct merchant `/api/init` 外層 form 必須送：

```text
store_uid
service
encry_data
```

- Reseller `/api/agent` 外層 form 必須送：

```text
agent_uid
service
encry_data
```

- 只有 profile 明確設定 `Credentials:AgentId` 時才走 agent/reseller mode。
- Direct merchant 不可同時送 `agent_uid`，避免 MyPay 用錯合約與金鑰路徑。
- encrypted `api/orders` payload 保留舊可用契約：

```text
store_uid
items
cost
user_id
order_id
ip
pfn
```

- 若 ChurchReport 舊流程沒有送 line items，adapter 會用產品名稱、金額、數量 1 產生預設品項。
- `user_id` 優先使用 metadata，再使用 customer name，最後退回 product order id。
- `ip` 優先使用 metadata，再使用 profile setting，最後退回 `127.0.0.1`。
- `pfn` 是 MyPay payment-function，不是 QPay `PayType`；不能直接把 `C` 當 MyPay pfn。

### 7.3 台新 TSPG

主要檔案：

```text
SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs
SpeechMessage.Payments/Providers/Taishin/TaishinRequestMapper.cs
SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs
SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs
SpeechMessage.Payments/Providers/Taishin/TaishinStatusMapper.cs
SpeechMessage.Payments/Providers/Taishin/TaishinModels.cs
```

責任：

- 建立 TSPG REST JSON payload。
- 呼叫台新 `auth.ashx` 與 `other.ashx` 類型 endpoint。
- 解析前端 form callback。
- 解析後端 JSON callback。
- 驗證 callback hash。
- 將 `ret_code` / `state` 轉成 normalized status。
- 回傳台新 backend callback 所需 JSON acknowledgement。

重要相容點：

- Callback hash 使用：

```text
StoreKey + transaction_id + order_id + state + StoreIV
```

再做 SHA256。

- Callback parser 接受 form 與 JSON 兩種 callback shape。
- Hash 驗證失敗會回 `PaymentErrorKind.SignatureInvalid`。
- ChurchReport controller 不再自行計算台新 hash。

## 8. ChurchReport adapter 與 workflow 檔案

ChurchReport 端保留的檔案不屬於 provider core，而是產品層薄轉接與相容 shim。

### 8.1 HTTP request 與 acknowledgement adapter

```text
ChurchReport/Payments/PaymentHttpRequestMapper.cs
ChurchReport/Payments/PaymentAcknowledgementResultMapper.cs
```

`PaymentHttpRequestMapper`：

- 將 ASP.NET `HttpRequest` 轉成 `PaymentCallbackRequest`。
- 啟用 buffering，避免 request body 被 MVC model binding 讀掉後 provider parser 拿到空 body。
- flatten query/form/header。
- 不解析 provider 欄位，不驗簽，不判斷 provider 狀態。

`PaymentAcknowledgementResultMapper`：

- 將核心 `PaymentCallbackAcknowledgement` 轉成 ASP.NET `IActionResult`。
- 支援 plain text、JSON、redirect、status code。
- 不決定產品成功頁或失敗頁。

### 8.2 建立付款與 workflow 結果 adapter

```text
ChurchReport/Payments/PaymentCreateRequestFactory.cs
ChurchReport/Payments/PaymentWorkflowResultMapper.cs
```

`PaymentCreateRequestFactory`：

- 將 ChurchReport workflow 收到的產品資料轉成 `PaymentCreateRequest`。
- 不組 provider SDK payload。
- 不做加密與簽章。

`PaymentWorkflowResultMapper`：

- 將 `PaymentCallbackResult` 轉成 ChurchReport 的 `PaymentWorkflowResult`。
- 供 CRM、LINE、View workflow 使用。
- 不解析 provider raw callback 或 provider status code。

### 8.3 QPay 相容層

```text
ChurchReport/Payments/QPayCreatePaymentGatewayAdapter.cs
ChurchReport/Payments/QPayReturnWorkflow.cs
ChurchReport/Payments/QPayProductWorkflowDispatcher.cs
ChurchReport/Payments/QPayWorkflowPaymentResult.cs
ChurchReport/Payments/LegacyQPayModels.cs
ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs
```

這些檔案存在的目的，是讓既有 ChurchReport QPay 前端、`QPayProcessor` 與產品 workflow 可以逐步改接新的 `IPaymentGateway`，而不是一次重寫全部奉獻流程。

`QPayCreatePaymentGatewayAdapter`：

- 將舊 QPay create order 輸入轉成 `PaymentCreateRequest`。
- 呼叫 `IPaymentGateway.CreatePaymentAsync(...)`。
- 將 `PaymentCreateResult` 映射回舊 `CreOrder`，供舊 UI 使用。
- 若信用卡/行動支付缺付款頁 URL，回 legacy failed status。
- 若 ATM 缺虛擬帳號，回 legacy failed status。
- 為定期定額補齊舊 UI 預設值：12 期、月週期、頻率 1。
- 若沒有 items，為 MyPay payload 產生預設 line item。

`QPayReturnWorkflow`：

- 接收 Sinopac query 後的 `PaymentStatusResult`。
- 轉成 `QPayWorkflowPaymentResult`。
- 交給 ChurchReport 既有費用或認獻/定期奉獻 processor。
- 沒有 product dispatcher 時提供 fallback view result。

`QPayProductWorkflowDispatcher`：

- 只負責呼叫 `QPayFeeProcessor` 或 `QPayDedicationBookingProcessor`。
- 保持 CRM/LINE/結果頁在 ChurchReport。

`LegacyQPayModels`：

- 保留 `CreOrder`、`CreOrderATMParamRes`、`CreOrderCardParamRes`、`CreOrderMobileParamRes`、`OrderMaintain`。
- 這些型別不屬於金流核心 public API，只是 ChurchReport 舊流程相容模型。

`QPayProcessor.PaymentGateway.cs`：

- 舊 `QPayProcessor` 的建立付款入口改接 `QPayCreatePaymentGatewayAdapter`。
- ChurchReport 只傳產品訂單、fee id、付款方式、callback URL、客戶名稱、定期定額參數。
- provider payload 由 `SpeechMessage.Payments` 決定。

### 8.4 Controller adapter

```text
ChurchReport/Controllers/MyPayController.cs
ChurchReport/Controllers/TSPGController.cs
ChurchReport/Controllers/QPayCardController.cs
```

`MyPayController`：

- 接收 MyPay backend callback。
- 用 `PaymentHttpRequestMapper` 建立 `PaymentCallbackRequest`。
- 呼叫 `IPaymentGateway.ParseCallbackAsync(...)`。
- 解析成功後用 normalized result 更新 CRM 與送 LINE。
- 依核心 acknowledgement 回覆 MyPay，例如 `8888`。

`TSPGController`：

- 接收台新 post-back 與 result-url。
- callback parsing 與 hash verification 交給台新 provider。
- 成功時更新 CRM fee，並發送 LINE 通知。
- 提供 create-payment / query-order 整合入口。

`QPayCardController`：

- 接收永豐 QPay return URL。
- callback parse 後用 PayToken 查詢最終付款狀態。
- 把查詢結果交給 `IQPayReturnWorkflow`。
- trace 中的 PayToken 只顯示頭尾，不輸出完整 token。

## 9. 從 ChurchReport 清掉的 provider 責任

本次抽離後，下列責任不應再留在 ChurchReport：

- 永豐 QPay 加密與簽章。
- 永豐 Nonce / OrderCreate / OrderPayQuery protocol。
- 永豐 create/query response DTO mapping。
- 永豐 callback parser 與狀態碼 mapping。
- 高鉅 MyPay form contract、payload encryption、callback parser、`prc` 狀態 mapping。
- 台新 TSPG JSON payload、callback hash verification、ret_code/state mapping。
- provider raw DTO 作為 ChurchReport 主要 workflow contract。

ChurchReport 仍可保留：

- ASP.NET Controller route。
- `HttpRequest` -> neutral callback request 的 mapping。
- acknowledgement descriptor -> `IActionResult` 的 mapping。
- CRM fee lookup/update。
- LINE notification。
- 奉獻/費用分類。
- 舊 QPay UI / processor 相容模型。
- 付款成功/失敗頁面與轉址。

## 10. 測試與保護的契約

### 10.1 核心 contract 測試

```text
SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs
```

保護：

- create/query/callback model 是 provider-neutral。
- query 使用 `ProviderOrderRef`，不使用 `PaymentToken`。
- callback request 不依賴 ASP.NET `HttpRequest`。
- acknowledgement 只描述 provider response shape。

### 10.2 設定測試

```text
SpeechMessage.Payments.Tests/Configuration/PaymentOptionsTests.cs
```

保護：

- 多 profile JSON binding。
- 空 profile name 使用 default profile。
- 未知 profile fail closed，不退回硬編 credential。

### 10.3 Gateway routing 測試

```text
SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs
```

保護：

- gateway 依 profile provider 選 provider。
- provider hint 與 profile 不一致時回 configuration error。
- 找不到 provider 時回 unsupported operation。

### 10.4 Sanitizer 測試

```text
SpeechMessage.Payments.Tests/Diagnostics/PaymentDiagnosticsSanitizerTests.cs
```

保護：

- PayToken、signature、StoreKey、卡號等敏感欄位遮蔽。
- `atm_pay_no` 保留完整值，因為它是使用者付款指示。

### 10.5 Provider 測試

```text
SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs
SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs
SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs
```

保護：

- MyPay `prc` mapping、callback acknowledgement、direct merchant / agent form、encrypted payload contract。
- Sinopac AES key、hosted payment URL、ATM 虛擬帳號、HTTP 400 diagnostics、callback sanitization。
- Taishin ret_code/state mapping、form/JSON callback、hash verification、create payload。

## 11. 抽離過程中修正的問題

### 11.1 永豐信用卡沒有跳到刷卡頁

問題：使用者點擊信用卡奉獻後，沒有跳到永豐信用卡輸入頁，而是回到原奉獻頁或出現 `Sinopac returned HTTP 400 BadRequest`。

處理：

- 修正永豐 AES key derivation，維持 legacy QPay Toolkit 的大寫 hex AES key。
- HTTP failure 顯示 route 與 response body 診斷。
- 若 hosted payment 成功回應缺付款頁 URL，create result 直接 fail closed。
- ChurchReport adapter 只有收到有效 `PaymentPageUrl` 才映射到 legacy `CardParam.CardPayURL`。

### 11.2 永豐 ATM/轉帳缺虛擬帳號

問題：ATM/匯款流程顯示銀行、分行、戶名，但帳號空白。

處理：

- Sinopac provider 將 `ATMParam.AtmPayNo` 映射到 `ProviderData["atm_pay_no"]`。
- Sanitizer 對 `atm_pay_no` 例外保留完整帳號。
- QPay legacy adapter 將 `ProviderData["atm_pay_no"]` 映射回 `CreOrder.ATMParam.AtmPayNo`。
- 若 provider 回成功但沒有 ATM 虛擬帳號，create result 直接 fail closed。

### 11.3 ATM 付款指示 LINE 通知可觀測性

問題：付款資訊頁出現，但 LINE 通知未送出或使用者無法收到時，原本不容易看出。

處理方向：

- LINE 通知仍保留在 ChurchReport，不移入金流核心。
- ATM 付款指示以頁面顯示為主，LINE delivery 失敗應由 ChurchReport workflow trace 或警示處理。
- 金流核心只保證 ATM 虛擬帳號與 provider data 正確跨出邊界。

### 11.4 定期定額缺預設扣款期數

問題：定期定額 UI 未送出可見預設值時，provider 可能拒絕建立付款。

處理：

- `QPayCreatePaymentGatewayAdapter` 對 recurring card 補上：

```text
DeductTotalNum = 12
PeriodType = M
DeductFreq = 1
```

### 11.5 `PAY_PROVIDER` 選高鉅仍跳永豐

問題：`PAY_PROVIDER` 設為高鉅時，流程仍走永豐。

處理：

- `ChurchReportPaymentProfileResolver` 將 `PAY_PROVIDER` 映射到 named profile。
- `高鉅金流` 對應 `MyPayProduction`。
- `台新金流` 對應 `TaishinSandbox`。
- `永豐金流` 對應 `JesusTest`。

### 11.6 MyPay 顯示金鑰過期或使用錯誤金鑰

問題：選高鉅金流後，MyPay 回應類似金鑰過期或使用錯誤金鑰。

處理：

- 修正 direct merchant `/api/init` 外層 form 使用 `store_uid`，不送 `agent_uid`。
- 只有 profile 設定 `AgentId` 時才走 reseller `/api/agent`。
- 修正 encrypted `api/orders` payload，保留 `items`、`user_id`、`ip`、`pfn` 等 MyPay 必要欄位。
- `pfn` 不再直接使用 QPay `PayType=C`。

## 12. 已新增與補強的繁體中文註解

本次依照維護需求，已在抽離後的核心與 adapter 補上繁體中文註解。註解重點不是逐行翻譯，而是說明：

- 這段程式屬於金流核心或 ChurchReport 產品層。
- 哪些 provider protocol 必須留在核心。
- 哪些 ASP.NET/CRM/LINE workflow 必須留在 ChurchReport。
- 為什麼保留 legacy QPay shape。
- 哪些欄位是相容舊流程的 metadata。
- 哪些錯誤要 fail closed。
- 哪些測試正在保護跨專案邊界。

主要補註解檔案：

```text
SpeechMessage.Payments/Abstractions/*.cs
SpeechMessage.Payments/Configuration/*.cs
SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs
SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs
SpeechMessage.Payments/Gateway/PaymentGateway.cs
SpeechMessage.Payments/Models/*.cs
SpeechMessage.Payments/Providers/MyPay/*.cs
SpeechMessage.Payments/Providers/Sinopac/*.cs
SpeechMessage.Payments/Providers/Taishin/*.cs
SpeechMessage.Payments/Providers/Common/DictionaryExtensions.cs
ChurchReport/Payments/*.cs
ChurchReport/Controllers/MyPayController.cs
ChurchReport/Controllers/TSPGController.cs
ChurchReport/Controllers/QPayCardController.cs
ChurchReport/WebServiceConnector/QPayProcessor/QPayProcessor.PaymentGateway.cs
ChurchReport/Startup.cs
SpeechMessage.Payments.Tests/**/*.cs
```

## 13. 已知限制

第一版抽離刻意不處理下列項目：

- MyPay query 尚未實作完整 provider 查詢。
- 退款、請款、取消、維護交易不在第一版 `IPaymentGateway`。
- 永豐 bill query、allotment query、order maintain 不在第一版。
- 金流後台管理 UI 不在第一版。
- Line Pay 仍在既有 `LinePayCSharp` 專案，未納入 `SpeechMessage.Payments`。
- ChurchReport 舊 QPay UI 與部分 processor 仍透過相容 adapter 使用 legacy shape，未一次重寫所有產品流程。

## 14. 驗證建議

每次修改金流核心或 adapter 後，建議至少執行：

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj --no-restore -v minimal -p:UseSharedCompilation=false

dotnet build ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore -v minimal -p:BaseOutputPath=.\artifacts\payment-report-comments-build\ -p:UseSharedCompilation=false

dotnet vstest "ChurchReport.MemberInfo.Tests\artifacts\payment-report-comments-build\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll" --TestCaseFilter:"FullyQualifiedName~Payments"

dotnet build ChurchReport.sln --no-restore -v minimal -p:BaseOutputPath=.\artifacts\solution-build-payment-report-comments\ -p:UseSharedCompilation=false

rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"

git diff --check
```

預期：

- `SpeechMessage.Payments.Tests` 通過。
- `ChurchReport.MemberInfo.Tests` payment 相關測試通過。
- solution build 通過。
- `SpeechMessage.Payments` 不出現 ChurchReport、ToolUtility、LINE、CRM、ASP.NET MVC、HttpRequest、DbContext 等產品層依賴。
- `LinePayCSharp` 不應有 diff。

## 15. 合併與部署注意事項

1. 合併回主線後，必須重新 build 並部署新的 `SpeechMessage.Payments.dll`。
2. ChurchReport 必須重啟，確保 DI 載入新的 provider 與 adapter。
3. `ChurchReport/appsettings.json` 的 `PAY_PROVIDER` 與 `Payment:Profiles` 必須同步。
4. 正式環境不可保留 placeholder credential，例如 `YOUR_MYPAY_IV_HERE`、`your_store_key`、`your_store_iv`。
5. 切換 provider 時優先改設定，不要改 Controller 或 provider 程式碼。
6. 新產品若要使用此金流核心，應直接引用 `SpeechMessage.Payments`，建立自己的 HTTP adapter 與產品 workflow，不要引用 ChurchReport 的 Controller 或 CRM/LINE 程式。

## 16. 結論

目前金流抽離已具備可維護的初步完成狀態：

- 通用金流核心已獨立成 `SpeechMessage.Payments`。
- 永豐、高鉅、台新 provider protocol 已集中到核心 provider 實作。
- ChurchReport 已改成透過 `IPaymentGateway` 與薄 adapter 使用核心。
- ASP.NET Controller、CRM、LINE、奉獻分類與結果頁仍留在 ChurchReport。
- 多 profile 設定已可透過 JSON 切換 provider。
- 測試已覆蓋主要 contract、provider mapping、callback、sanitization 與相容性問題。
- 本次已補上繁體中文維護註解與完整抽離報告，降低後續維護時誤破壞邊界的風險。
