# LINE 模組重構進度與未來產品整合說明

## 1. 目前進度總覽

目前工作分支是 `Jesus_5.1.6.WorktreeRefactorLine`，開發位置在 `.worktrees\Jesus_5.1.6.WorktreeRefactorLine`。

本輪 LINE 重構已完成兩個層次：

1. 共用通知流程已建立並提交：
   - `fcbcfb2e feat: extract reusable LINE notification workflow`
   - `48d8d14e fix: disambiguate LINE processor DI registration`
   - `5e43e1fa 統一LINE付款通知推播路徑並補齊測試`
   - `b5eddb55 feat: add LINE image and flex notification content wrappers`

2. 產品友善 LINE 訊息 API 已完成並提交：
   - `92f7370b feat: add product-friendly LINE message factories`
   - 設計文件：`docs/superpowers/specs/2026-07-03-line-product-friendly-message-api-design.md`
   - 實作計畫：`docs/superpowers/plans/2026-07-03-line-product-friendly-message-api.md`
   - CCG review：`.ccg/tasks/archive/2026-07/line-product-friendly-message-api/review.md`

驗證結果：

- `LineMessagingProcessor.Workflows.Tests`：33 tests passed。
- `Line.Messaging.Tests`：32 tests passed。
- `ChurchReport.sln`：build succeeded，0 warnings / 0 errors。

## 2. 這次改善了什麼

### 2.1 LINE 呼叫路徑更集中

重構前，不同產品流程可能直接呼叫不同 LINE 工具類別或低階 SDK。這會造成重複、錯誤處理不一致，以及未來產品難以重用。

現在共用呼叫面集中到：

- `Line.Messaging`：LINE 官方訊息模型與序列化形狀。
- `LineMessagingProcessor`：LINE API 呼叫。
- `LineMessagingProcessor.Workflows`：可重用的通知 workflow 與產品友善訊息 factory。
- `LineMessagingProcessor.AspNetCore`：ASP.NET Core 整合與 DI 註冊。

ChurchReport 只保留自己的 CRM、付款、奉獻與畫面流程，不把產品邏輯塞進共用 LINE 模組。

### 2.2 未來產品不用直接組 LINE JSON

現在未來產品可以用 `LineNotificationContent` 與相關 factory 建立訊息，不需要知道低階 LINE JSON 欄位與 SDK 建構細節。

目前已支援的產品友善 API 包含：

- `TextMessage(...)`
- `TextMessageV2(...)`
- `ImageMessage(...)`
- `FlexMessage(...)`
- `StickerMessage(...)`
- `VideoMessage(...)`
- `AudioMessage(...)`
- `LocationMessage(...)`
- `CouponMessage(...)`
- `ConfirmTemplateMessage(...)`
- `ButtonsTemplateMessage(...)`
- `CarouselTemplateMessage(...)`
- `ImageCarouselTemplateMessage(...)`
- `ImagemapMessage(...)`

輔助 factory：

- `LineQuickReplyFactory`
- `LineTemplateActionFactory`
- `LineCarouselColumnFactory`
- `LineImagemapActionFactory`

### 2.3 驗證規則集中在共用層

共用 factory 會在送出 HTTP 前先驗證常見錯誤：

- 必填字串不可空白。
- 圖片、影片、音訊、Imagemap URL 必須是絕對 HTTPS URL。
- Quick Reply 最多 13 個 item。
- Confirm Template 必須剛好 2 個 action。
- Buttons Template 必須 1 到 4 個 action。
- Carousel 最多 10 個 column。
- Carousel column 必須 1 到 3 個 action。
- Imagemap 必須 1 到 50 個 action。
- Location latitude / longitude 必須在合理範圍。
- Coupon `deliveryTag` 不可超過 LINE 限制。

這讓錯誤能在產品伺服器內提早被攔下，而不是送到 LINE API 後才得到模糊錯誤。

### 2.4 ASP.NET Core DI 啟動更穩

先前 `LineMessagingProcessorClass` 曾因多個 constructor 導致 ASP.NET Core DI 無法判斷要用哪個 constructor。已修正 DI 註冊，使未來 ASP.NET Core 產品引用 LINE 模組時不會因 constructor ambiguity 啟動失敗。

## 3. 對未來產品整合的幫助

這次重構對未來產品有直接幫助，因為它把「LINE 共用能力」和「產品業務流程」切開。

未來產品如：

- 建設公司維修系統
- 協會會員系統
- 發票收款系統

都可以直接引用共用 LINE 專案，使用同一套通知 workflow 和訊息 factory。每個產品只需要處理自己的業務資料，例如維修單號、會員續約、發票號碼、付款狀態，不需要複製 ChurchReport 的 LINE 工具程式。

這符合目前的設計原則：

- 少特殊情況。
- 資料流清楚。
- 一個東西只做一件事。
- 共用模組不依賴 ChurchReport。
- 產品流程不散落低階 LINE API 細節。

## 4. 未來產品如何整合

### 4.1 專案引用

未來 ASP.NET Core 產品建議引用：

```xml
<ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
<ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
<ProjectReference Include="..\LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj" />
<ProjectReference Include="..\LineMessagingProcessor.AspNetCore\LineMessagingProcessor.AspNetCore.csproj" />
```

若產品不需要 ASP.NET Core DI helper，可以不引用 `LineMessagingProcessor.AspNetCore`，但仍建議優先使用已存在的整合層，避免每個產品手寫不同 DI 設定。

### 4.2 設定 Channel Access Token

產品應該在設定檔或安全設定來源提供 LINE Messaging API token。正式環境不要把 token 寫死在程式碼中，應使用環境變數、Secret Manager、CI/CD secret 或正式機器安全設定。

概念如下：

```json
{
  "LineMessaging": {
    "ChannelAccessToken": "YOUR_CHANNEL_ACCESS_TOKEN",
    "Endpoint": "https://api.line.me/v2"
  }
}
```

實際 key 名稱應以 `LineMessagingProcessor.AspNetCore` 目前提供的設定擴充方法為準。

### 4.3 注入共用 workflow

產品服務只需要依賴 `ILineNotificationWorkflow`：

```csharp
public sealed class RepairNotificationService
{
    private readonly ILineNotificationWorkflow _lineNotificationWorkflow;

    public RepairNotificationService(ILineNotificationWorkflow lineNotificationWorkflow)
    {
        _lineNotificationWorkflow = lineNotificationWorkflow;
    }

    public Task NotifyAssignedAsync(string lineUserId, string repairOrderNo)
    {
        return _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(lineUserId),
            Content = LineNotificationContent.TextMessage($"維修案件 {repairOrderNo} 已派工。"),
            RetryKey = $"repair:{repairOrderNo}:assigned"
        });
    }
}
```

### 4.4 建立不同產品訊息

建設公司維修系統可以用 Buttons Template：

```csharp
var content = LineNotificationContent.ButtonsTemplateMessage(
    altText: "維修案件狀態更新",
    text: "您的維修案件已派工，請選擇後續操作。",
    title: "維修通知",
    thumbnailImageUrl: null,
    actions: new[]
    {
        LineTemplateActionFactory.Uri("查看案件", "https://repair.example.com/orders/1001"),
        LineTemplateActionFactory.Message("聯絡客服", "我要聯絡客服")
    });
```

協會會員系統可以用 Text v2：

```csharp
var content = LineNotificationContent.TextMessageV2("會員續約提醒");
```

發票收款系統可以用 Coupon 或 Template：

```csharp
var content = LineNotificationContent.CouponMessage("coupon-202607", "invoice-payment");
```

ChurchReport 付款通知可以繼續用 Text、Image、Flex 或 Template：

```csharp
var content = LineNotificationContent.TextMessage("您的奉獻付款已完成，謝謝您。");
```

## 5. 還沒有完成的部分

目前不能宣稱「LINE 官方所有 API 都補完」。本輪完成的是「未來產品最常用的訊息內容與通知 workflow 共用化」。

尚未完成或建議後續分批處理：

- 尚未把 ChurchReport 所有舊 LINE 呼叫點全部改走新 workflow。
- 尚未補完 LINE 官方所有 endpoint。
- 尚未完整處理 webhook event parsing 與產品事件路由。
- 尚未建立跨產品共用的業務通知模板 catalog。
- Claude review 目前仍因 CLI/tooling 層失敗無法產出有效審查，但 Gemini review 與本機驗證已通過。

## 6. 下一步建議

建議下一步不要追求一次補完 LINE 官方所有 API，而是先把 ChurchReport 現有 LINE 呼叫點繼續收斂：

1. 盤點 `PushUtility`、`ReplyUtility`、`LineUtilityClass` 等仍直接呼叫 LINE 的地方。
2. 優先把付款通知、必要通知、錯誤不能被吞掉的流程改走 `ILineNotificationWorkflow`。
3. 保留 ChurchReport 的 CRM、付款、奉獻流程在 ChurchReport，不放進共用 LINE 專案。
4. 等未來產品真的需要，再補下一批官方 API。

## 7. 結論

這次進度對未來產品整合有實質幫助。

最大的價值不是單純新增幾個 message type，而是建立了可重用的 LINE 通知邊界：

- 共用 LINE 模組處理訊息模型、驗證、發送。
- ChurchReport 與未來產品只處理自己的業務流程。
- 未來產品不用複製 ChurchReport 舊工具類別。
- 測試與驗證集中在共用層，降低每個產品各自踩 LINE API 細節的機率。

