# LINE 共用化抽離 Phase 2 設計規格

日期：2026-07-03
分支：Jesus_5.1.6.WorktreeRefactorLine
狀態：已完成 brainstorming，等待 implementation plan

## 1. 目標

Phase 2 的目標不是再修一兩個 LINE 呼叫點，而是把 ChurchReport 現有 LINE 相關程式整理成未來產品可以共用的模組。

未來產品包含建設公司維修系統、協會會員系統、發票收款系統等 ASP.NET Core 專案。這些產品應能引用共用 LINE 專案，取得通知、收件者、錯誤處理、重試與 DI 註冊能力，而不需要複製 ChurchReport 的 `PushUtility`、`LineUtilityClass` 或散落的 `LineMessagingClient` 建構邏輯。

## 2. 採用方案

採用「共用核心 + ChurchReport 主要通知路徑同步接上」。

這不是單點小修，也不是一次搬完所有 LINE 程式。Phase 2 會建立乾淨共用核心，然後用 ChurchReport 主要通知流程驗證這個核心是否真的能給其他產品重用。

第一批 ChurchReport 接入包含：

- 付款 / 奉獻通知
- 一般 `PushUtility` 文字通知
- LINE 綁定 / 會員身份通知

## 3. 架構邊界

LINE 能力分成四層。

### 3.1 `Line.Messaging`

`Line.Messaging` 是 LINE 官方 API SDK 層。

它負責：

- LINE endpoint
- HTTP header
- JSON request / response
- 官方 message model
- webhook model
- LINE API 回應錯誤

它不負責：

- ChurchReport
- CRM
- 付款 / 奉獻
- 會員 / 小組
- notification workflow
- ASP.NET Core DI

### 3.2 `LineMessagingProcessor`

`LineMessagingProcessor` 是 LINE SDK 的共用 adapter 層。

它負責：

- Send
- Reply
- Profile
- Group / Room
- RichMenu
- RetryKey / Reliable Push
- 共用 validation
- 對 `Line.Messaging` 的穩定包裝

它可以依賴 `Line.Messaging`，但不能依賴 ChurchReport、CRM、付款、奉獻、會員或小組語意。

### 3.3 `LineMessagingProcessor.Workflows`

新增 `LineMessagingProcessor.Workflows` 專案。

這一層負責未來產品都會用到的「通知工作流」抽象：

- 收件者
- 通知內容
- 發送模式
- retry key
- metadata
- 發送結果
- 失敗結果或例外

它不依賴 ChurchReport，也不依賴 ASP.NET Core。

### 3.4 `LineMessagingProcessor.AspNetCore`

新增 `LineMessagingProcessor.AspNetCore` 專案。

這一層負責 ASP.NET Core 整合：

- options / configuration binding
- DI registration
- `services.AddLineMessagingProcessor(...)`
- `services.AddLineNotificationWorkflow(...)`

ChurchReport 與未來 ASP.NET Core 產品都透過這層快速註冊 LINE 共用服務。

### 3.5 ChurchReport

ChurchReport 保留產品流程：

- CRM 查詢
- 奉獻資料查詢
- 付款通知內容組裝
- 小組通知內容組裝
- 會員綁定與權限判斷
- Controller
- View
- LIFF 前端流程
- session / cookie / route

ChurchReport 不應再到處自行建立 `LineMessagingClient` 送通知。ChurchReport 應集中呼叫共用 workflow 或 processor。

## 4. 共用通知模型

`LineMessagingProcessor.Workflows` 對外提供產品友善模型，避免未來產品一開始就必須理解 LINE SDK 的所有 message 類別。

### 4.1 `LineNotificationRequest`

一次通知請求。

包含：

- `Recipient`
- `Content`
- `RetryKey`
- `Metadata`
- 發送模式或必要通知標記

### 4.2 `LineNotificationRecipient`

表示通知對象。

第一階段支援：

- `User(lineUserId)`
- `Users(IEnumerable<string>)`
- `Group(groupId)`
- `Room(roomId)`

未來可擴充 broadcast / narrowcast，但 Phase 2 不把 broad official API expansion 當成目標。

### 4.3 `LineNotificationContent`

表示通知內容。

第一階段支援：

- `Text(string message)`
- `SdkMessages(IReadOnlyList<ISendMessage> messages)`

`Text(...)` 是給未來產品最常用的簡單入口。`SdkMessages(...)` 是 escape hatch，讓 ChurchReport 既有 Flex、Template、Image、Sticker 等複雜 LINE 訊息可以逐步接上，而不需要重造一整套 LINE message DTO。

### 4.4 `LineNotificationResult`

表示發送結果。

包含：

- `Succeeded`
- `Status`
- `ErrorCode`
- `ErrorMessage`
- `ProviderResponse`
- `Recipient`
- `RetryKey`
- `Metadata`

### 4.5 `LineNotificationStatus`

第一階段狀態：

- `Succeeded`
- `ValidationFailed`
- `ProviderRejected`
- `ProviderUnavailable`
- `UnexpectedError`

## 5. 發送入口與錯誤處理

共用 workflow 提供兩種入口。

### 5.1 `SendAsync(...)`

`SendAsync(LineNotificationRequest request)` 不丟例外。

發送成功或失敗都回傳 `LineNotificationResult`。這適合一般提醒、可選通知、失敗不應中斷主流程的情境。

### 5.2 `SendOrThrowAsync(...)`

`SendOrThrowAsync(LineNotificationRequest request)` 在失敗時丟 `LineNotificationException`。

這適合付款指示、重要狀態、不能靜默失敗的通知。

### 5.3 錯誤分類

- 空 LINE ID、空訊息、空收件者：workflow 層 fail fast，回傳 `ValidationFailed` 或丟 `LineNotificationException`。
- LINE API 拒絕：轉成 `ProviderRejected`。
- timeout / network failure：轉成 `ProviderUnavailable`。
- 未預期錯誤：轉成 `UnexpectedError`。

共用模組不寫 ChurchReport trace、不更新 CRM、不更新付款狀態。ChurchReport 在收到 result 或 exception 後自行決定產品流程要怎麼處理。

## 6. ChurchReport 第一批接入範圍

### 6.1 付款 / 奉獻通知

目標是讓付款通知流程改用 `LineMessagingProcessor.Workflows`。

接入範圍：

- 成功付款通知
- 失敗付款通知
- ATM / 匯款付款指示
- 定期定額付款結果

原則：

- 必要付款指示使用 `SendOrThrowAsync(...)`。
- 一般付款結果通知可依既有語意使用 `SendAsync(...)` 或 `SendOrThrowAsync(...)`。
- ChurchReport 繼續負責 CRM 收費單更新、奉獻資料查詢、付款文字內容組裝與 LINE ID 來源。

### 6.2 一般 `PushUtility` 文字通知

目標是把最常見的文字推播集中到 workflow。

接入範圍：

- `PushUtility.SendMessage(...)`
- 可安全轉成 `LineNotificationContent.Text(...)` 的文字通知

第一批不搬：

- Flex 複雜樣板
- RichMenu 建立 / 上傳 / 綁定
- ImageMap
- Template Carousel
- `.Wait()` 同步阻塞路徑的大規模重寫

`PushUtility` 第一階段保留 public API，但內部逐步變成 ChurchReport 舊 API wrapper。

### 6.3 LINE 綁定 / 會員身份通知

目標是讓會員綁定與身份通知使用共用 workflow / processor。

接入範圍：

- LINE profile 查詢使用 processor
- 綁定成功通知
- 綁定失敗通知
- 會員身份提示通知

ChurchReport 保留：

- CRM contact 查詢
- 會員權限判斷
- session
- controller
- view
- LIFF 前端流程

## 7. Implementation Batches

Phase 2 拆成四個 implementation batches。每批都必須能獨立測試、獨立 review、必要時獨立回退。

### Batch 1：共用核心與 ASP.NET Core 整合

新增：

- `LineMessagingProcessor.Workflows`
- `LineMessagingProcessor.AspNetCore`
- 對應測試專案

內容：

- `LineNotificationRequest`
- `LineNotificationRecipient`
- `LineNotificationContent`
- `LineNotificationResult`
- `LineNotificationStatus`
- `LineNotificationException`
- `ILineNotificationWorkflow`
- `LineNotificationWorkflow`
- `LineMessagingProcessorOptions`
- DI extension methods

驗收：

- workflow 可以發送 text notification。
- workflow 可以接受 SDK messages。
- `SendAsync` 失敗回傳 result。
- `SendOrThrowAsync` 失敗丟 exception。
- ASP.NET Core 專案可以透過 DI extension 註冊。
- 共用專案不得 reference ChurchReport、CRM、ASP.NET Controller、DbContext 或付款專案。

### Batch 2：付款 / 奉獻通知接入

處理：

- `PaymentNotificationService`
- `DonationPaymentProcessor` 內必要付款指示通知
- `DonationFeePaymentProcessor`
- `RecurringDonationPaymentProcessor`

驗收：

- 付款通知不再直接散落建立 `LineMessagingClient`。
- LINE 發送失敗在必要通知流程可被觀察。
- 原本付款頁面、CRM 更新與付款狀態流程不被破壞。

### Batch 3：一般 `PushUtility` 文字通知接入

處理：

- `ChurchReport/Tools/PushUtility.cs`
- 使用 `PushUtility.SendMessage(...)` 的一般文字通知呼叫點

驗收：

- 舊呼叫點不用立刻改簽名。
- `PushUtility` 開始變成 wrapper。
- 文字通知可走共用 workflow。
- 除非路徑明確改為必要通知，否則現有流程不因通知失敗行為改變而中斷。

### Batch 4：LINE 綁定 / 會員身份通知接入

處理：

- `MemberInfoController` 直接 profile 查詢
- LINE 綁定相關服務
- 會員身份通知或提示訊息
- `LineUtilityClass` 中可安全抽出的 profile / text notify 路徑

驗收：

- 會員綁定流程仍可查 profile。
- 綁定通知可走 workflow。
- 共用專案沒有 ChurchReport 依賴。
- 直接 `LineMessagingClient` 使用點明顯減少。

## 8. Guardrails

- 不把 CRM、付款、奉獻、會員、小組語意放進 `LineMessagingProcessor.Workflows` 或 `LineMessagingProcessor.AspNetCore`。
- 不修改 `LinePayCSharp`。
- 不把 LIFF `.cshtml` / JavaScript 搬進後端共用模組。
- 不做 broad official LINE API expansion。
- 不一次搬完 `LineUtilityClass`。
- 不在共用模組中隱藏全域狀態。
- 不新增只有 ChurchReport 才懂的 abstraction。
- 檔案必須 UTF-8 without BOM + CRLF。
- 不提交 `bin/`、`obj/`、`artifacts/`。

## 9. Validation

每個 batch 完成後至少執行：

- 對應測試專案。
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`。
- boundary scan：共用 LINE 專案不得含 `ChurchReport`、`Microsoft.Xrm`、`IOrganizationService`、`Entity`、`Controller`、`IActionResult`、`DbContext`。
- 搜尋 ChurchReport 直接 `LineMessagingClient` 使用點，確認該 batch 的接入點已收斂。
- Gemini + Claude 雙模型 review。
- touched text files encoding check。

## 10. 下一步

依本 spec 撰寫 implementation plan。Plan 必須分批描述 Batch 1 到 Batch 4，且每批都要有 TDD、驗證、boundary scan、review 與可回退範圍。
