# LINE Product-Friendly Message API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reusable, product-friendly LINE message factories for the official message types most useful to ChurchReport and future ASP.NET Core products.

**Architecture:** Keep official JSON/message contracts in `Line.Messaging`; keep reusable product-friendly factory methods in `LineMessagingProcessor.Workflows`; keep ChurchReport product workflows untouched in this slice. The implementation is intentionally thin: validate inputs, create SDK message objects, and let the existing workflow send them.

**Tech Stack:** C#/.NET 10, xUnit, FluentAssertions, Newtonsoft.Json serialization through existing `LineMessagingProcessor.Workflows.Tests`.

---

### Task 1: Add RED Tests For SDK Message Gaps

**Files:**
- Modify: `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs`
- Create: `Line.Messaging/Messages/TextV2Message.cs`
- Create: `Line.Messaging/Messages/CouponMessage.cs`
- Modify: `Line.Messaging/Messages/MessageType.cs`

- [ ] **Step 1: Add failing tests for Text v2 and Coupon wrapper payloads**

Add tests that call the wished-for API:

```csharp
[Fact]
public async Task SendAsync_posts_text_v2_message_created_by_product_friendly_wrapper()
{
    var handler = new CapturingHttpMessageHandler();
    var workflow = CreateWorkflow(handler);

    var result = await workflow.SendAsync(new LineNotificationRequest
    {
        Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
        Content = LineNotificationContent.TextMessageV2("hello v2")
    });

    result.Succeeded.Should().BeTrue();

    var body = JObject.Parse(handler.Bodies[0]);
    body["messages"]![0]!["type"]!.Value<string>().Should().Be("textV2");
    body["messages"]![0]!["text"]!.Value<string>().Should().Be("hello v2");
}

[Fact]
public async Task SendAsync_posts_coupon_message_created_by_product_friendly_wrapper()
{
    var handler = new CapturingHttpMessageHandler();
    var workflow = CreateWorkflow(handler);

    var result = await workflow.SendAsync(new LineNotificationRequest
    {
        Recipient = LineNotificationRecipient.User("U1234567890abcdef"),
        Content = LineNotificationContent.CouponMessage("coupon-001", "invoice-reminder")
    });

    result.Succeeded.Should().BeTrue();

    var body = JObject.Parse(handler.Bodies[0]);
    body["messages"]![0]!["type"]!.Value<string>().Should().Be("coupon");
    body["messages"]![0]!["couponId"]!.Value<string>().Should().Be("coupon-001");
    body["messages"]![0]!["deliveryTag"]!.Value<string>().Should().Be("invoice-reminder");
}
```

- [ ] **Step 2: Run the RED tests**

Run:

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "SendAsync_posts_text_v2_message_created_by_product_friendly_wrapper|SendAsync_posts_coupon_message_created_by_product_friendly_wrapper"
```

Expected: compile fails because `TextMessageV2` and `CouponMessage` wrappers do not exist.

- [ ] **Step 3: Implement minimal SDK message objects and wrappers**

Add `MessageType.TextV2` and `MessageType.Coupon`. Add `TextV2Message` and `CouponMessage` implementing `ISendMessage`. Add corresponding `LineNotificationContent` factory methods.

- [ ] **Step 4: Verify GREEN**

Run the same filtered test command.

Expected: pass.

### Task 2: Add Quick Reply And Template Factories

**Files:**
- Create: `LineMessagingProcessor.Workflows/LineTemplateActionFactory.cs`
- Create: `LineMessagingProcessor.Workflows/LineQuickReplyFactory.cs`
- Create: `LineMessagingProcessor.Workflows/LineCarouselColumnFactory.cs`
- Modify: `LineMessagingProcessor.Workflows/LineNotificationContent.cs`
- Modify: `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs`

- [ ] **Step 1: Add failing tests for quick reply and templates**

Tests must prove:

- `TextMessage(..., quickReply)` serializes `quickReply.items`.
- `ConfirmTemplateMessage(...)` serializes `template.type = confirm` and exactly two actions.
- `ButtonsTemplateMessage(...)` serializes `template.type = buttons`.
- `CarouselTemplateMessage(...)` serializes `template.type = carousel` and columns.
- `ImageCarouselTemplateMessage(...)` serializes `template.type = image_carousel`.

- [ ] **Step 2: Run RED tests**

Run:

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "QuickReply|Template"
```

Expected: compile fails because the factories and overloads do not exist.

- [ ] **Step 3: Implement minimal factories**

Factories should be small static classes:

```csharp
public static class LineTemplateActionFactory
{
    public static ITemplateAction Message(string label, string text) => new MessageTemplateAction(label, text);
    public static ITemplateAction Postback(string label, string data, string? displayText = null) => new PostbackTemplateAction(label, data, displayText);
    public static ITemplateAction Uri(string label, string uri) => new UriTemplateAction(label, uri);
}
```

`LineQuickReplyFactory.Create(...)` validates 1 to 13 items and returns `QuickReply`.

- [ ] **Step 4: Verify GREEN**

Run the filtered test command and then the full workflow test project.

### Task 3: Add Thin Wrappers For Media, Sticker, Location, And Imagemap

**Files:**
- Create: `LineMessagingProcessor.Workflows/LineImagemapActionFactory.cs`
- Modify: `LineMessagingProcessor.Workflows/LineNotificationContent.cs`
- Modify: `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs`

- [ ] **Step 1: Add failing payload tests**

Add tests for:

- `StickerMessage(packageId, stickerId)`
- `VideoMessage(originalContentUrl, previewImageUrl)`
- `AudioMessage(originalContentUrl, durationMilliseconds)`
- `LocationMessage(title, address, latitude, longitude)`
- `ImagemapMessage(baseUrl, altText, width, height, actions)`

- [ ] **Step 2: Run RED tests**

Run:

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter "Sticker|Video|Audio|Location|Imagemap"
```

Expected: compile fails because wrapper methods and imagemap factory do not exist.

- [ ] **Step 3: Implement minimal wrappers**

Add static factory methods to `LineNotificationContent`. Reuse the same HTTPS validator for image, video, audio, and imagemap URL fields.

- [ ] **Step 4: Verify GREEN**

Run the filtered test command and then the full workflow test project.

### Task 4: Validation, Review, And Commit

**Files:**
- Modify: `.ccg/tasks/line-product-friendly-message-api/task.json`
- Create: `.ccg/tasks/line-product-friendly-message-api/review.md`
- Archive: `.ccg/tasks/archive/2026-07/line-product-friendly-message-api/`

- [ ] **Step 1: Run full validation**

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

- [ ] **Step 2: Run dual-model CCG review**

Use Gemini and Claude reviewer roles on `git diff`. If Claude fails for quota/tooling and Gemini plus local validation pass, record that as non-blocking per user instruction.

- [ ] **Step 3: Normalize touched text files**

All touched `.cs`, `.md`, and `.json` files must be UTF-8 without BOM and CRLF.

- [ ] **Step 4: Archive CCG task and commit**

```powershell
git add -- Line.Messaging LineMessagingProcessor.Workflows LineMessagingProcessor.Workflows.Tests docs\superpowers .ccg\tasks
git commit -m "feat: add product-friendly LINE message factories"
```

