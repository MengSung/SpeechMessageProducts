# LINE Reliable Notification Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This repository is in inline mode, so do not dispatch implement/check subagents.

**Goal:** Add a reusable LINE reliable-notification adapter and wire one ChurchReport payment notification path to use deterministic retry keys.

**Architecture:** `Line.Messaging` remains the only layer that knows the `X-Line-Retry-Key` HTTP header. `LineMessagingProcessor` gains a thin reusable reliable push method that accepts a business retry key and delegates to the SDK overload. ChurchReport owns payment-event identity and builds deterministic retry keys before calling the processor adapter.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, Newtonsoft.Json, existing `Line.Messaging`, `LineMessagingProcessor`, `ChurchReport`, and focused test projects.

---

## Scope And Boundaries

This plan implements the approved A+B direction from `docs/superpowers/specs/2026-07-02-line-reliable-notification-adapter-design.md`.

Included:

1. Add one reliable push adapter method to `LineMessagingProcessorClass`.
2. Add an injectable SDK-client constructor to make the processor testable without live LINE calls.
3. Add a small `LineMessagingProcessor.Tests` project for processor request-capture tests.
4. Add a deterministic payment notification retry-key helper in `PaymentNotificationService`.
5. Add an overload to `PaymentNotificationService.SendLineMessage(...)` that accepts retry key.
6. Wire one ChurchReport payment notification vertical slice through the reliable adapter.
7. Preserve old send methods and existing non-payment notification paths.

Excluded:

- Do not implement P2 official API expansion.
- Do not add Audience, Narrowcast, quote token, sender, or mention APIs.
- Do not touch LINE login, LIFF, webhook handling, or general reply-message flows.
- Do not move ChurchReport CRM/payment business logic into `Line.Messaging` or `LineMessagingProcessor`.
- Do not migrate every `PushUtility.SendMessage(...)` call site.
- Do not change payment provider callback success/failure semantics.
- Do not edit unrelated `.ccg/tasks/qpay-model-boundary-brainstorm/.turns.json`.

## Files And Responsibilities

- Modify `LineMessagingProcessor/LineMessagingProcessor.csproj`: add a project reference to `Line.Messaging` so the processor can delegate to the SDK instead of manually owning the reliable-push protocol.
- Modify `LineMessagingProcessor/LineMessagingProcessorClass.cs`: add an optional SDK client field, a testable SDK-client constructor, and `SendReliableMessageAsync(string userId, string message, string? retryKey)`.
- Create `LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj`: focused test project for the processor adapter.
- Create `LineMessagingProcessor.Tests/LineMessagingProcessorReliableNotificationTests.cs`: tests that prove retry key reaches the SDK request and existing send behavior can remain separate.
- Modify `ChurchReport/ChurchReport.csproj`: add a project reference to `LineMessagingProcessor` if not already present.
- Modify `ChurchReport/Services/PaymentNotificationService.cs`: add deterministic retry-key builder and reliable send overload; update payment-result notification calls to use it when an order identifier exists.
- Modify `ChurchReport.sln`: include `LineMessagingProcessor.Tests` if the solution requires explicit project registration.

## Current Code Facts

- `Line.Messaging` already has retry-key overloads for push, multicast, and broadcast.
- `LineMessagingProcessorClass.SendMessage(string UserId, string Message)` currently sends through RestSharp and must remain compatible.
- `PaymentNotificationService.SendLineMessage(string lineId, string message)` currently creates `LineMessagingClient`, wraps it in `PushUtility`, and blocks on `.Wait()`.
- `PaymentNotificationService` has payment-result methods that resolve `new_lineid`, build payment messages, then call `SendLineMessage(lineId, message)`.
- Existing direct notification calls in `DonationFeePaymentProcessor` are intentionally out of this slice.

---

### Task 1: Add Processor Test Project And Failing Reliable-Push Tests

**Files:**
- Create: `LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj`
- Create: `LineMessagingProcessor.Tests/LineMessagingProcessorReliableNotificationTests.cs`

- [ ] **Step 1: Create the test project**

Create `LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="FluentAssertions" Version="8.8.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create failing adapter tests**

Create `LineMessagingProcessor.Tests/LineMessagingProcessorReliableNotificationTests.cs`:

```csharp
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;

namespace LineMessagingProcessor.Tests;

public sealed class LineMessagingProcessorReliableNotificationTests
{
    [Fact]
    public async Task Send_reliable_message_passes_retry_key_to_line_sdk()
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = CreateSdkClient(handler);
        var processor = new LineMessagingProcessor.LineMessagingProcessorClass(sdkClient);

        await processor.SendReliableMessageAsync(
            "U1234567890abcdef",
            "payment received",
            "churchreport:payment:order-1001:paid:payer-line-notice");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("churchreport:payment:order-1001:paid:payer-line-notice");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
        body["messages"]!.Should().HaveCount(1);
        body["messages"]![0]!["text"]!.Value<string>().Should().Be("payment received");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_reliable_message_with_empty_retry_key_does_not_send_retry_header(string? retryKey)
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = CreateSdkClient(handler);
        var processor = new LineMessagingProcessor.LineMessagingProcessorClass(sdkClient);

        await processor.SendReliableMessageAsync(
            "U1234567890abcdef",
            "payment received",
            retryKey);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "message", "retry-key", "UserId is required.")]
    [InlineData("", "message", "retry-key", "UserId is required.")]
    [InlineData("U123", null, "retry-key", "Message is required.")]
    [InlineData("U123", "", "retry-key", "Message is required.")]
    public async Task Send_reliable_message_rejects_missing_required_values(string? userId, string? message, string? retryKey, string expectedMessage)
    {
        var handler = new CapturingHttpMessageHandler();
        var sdkClient = CreateSdkClient(handler);
        var processor = new LineMessagingProcessor.LineMessagingProcessorClass(sdkClient);

        var act = () => processor.SendReliableMessageAsync(userId!, message!, retryKey);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage(expectedMessage);
        handler.Requests.Should().BeEmpty();
    }

    private static LineMessagingClient CreateSdkClient(CapturingHttpMessageHandler handler)
    {
        return new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
```

- [ ] **Step 3: Run the processor tests and verify RED**

Run:

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal
```

Expected: build fails because `LineMessagingProcessorClass(LineMessagingClient)` and `SendReliableMessageAsync(...)` do not exist yet.

### Task 2: Implement The Processor Reliable Adapter

**Files:**
- Modify: `LineMessagingProcessor/LineMessagingProcessor.csproj`
- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`

- [ ] **Step 1: Add SDK project reference**

In `LineMessagingProcessor/LineMessagingProcessor.csproj`, add this item group if it is not already present:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Add SDK using and field**

At the top of `LineMessagingProcessor/LineMessagingProcessorClass.cs`, add:

```csharp
using Line.Messaging;
```

Inside `LineMessagingProcessorClass`, near `_restClient`, add:

```csharp
        private readonly LineMessagingClient? _lineMessagingClient;
```

- [ ] **Step 3: Preserve existing constructors and initialize the optional SDK client**

Update the existing token constructor so it explicitly sets the SDK client field to null:

```csharp
        public LineMessagingProcessorClass(string channelAccessToken)
        {
            _channelAccessToken = NormalizeBearerToken(channelAccessToken);
            var options = new RestClientOptions("https://api.line.me/v2/bot");
            _restClient = new RestClient(options);
            _lineMessagingClient = null;
        }
```

Add this testable constructor after the existing configuration constructor:

```csharp
        public LineMessagingProcessorClass(LineMessagingClient lineMessagingClient)
        {
            _lineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
            _channelAccessToken = string.Empty;
            var options = new RestClientOptions("https://api.line.me/v2/bot");
            _restClient = new RestClient(options);
        }
```

- [ ] **Step 4: Add the reliable push adapter method**

Add this method near the existing `SendMessage(string UserId, string Message)` method:

```csharp
        public async Task SendReliableMessageAsync(string UserId, string Message, string? retryKey)
        {
            if (string.IsNullOrWhiteSpace(UserId))
            {
                throw new ArgumentException("UserId is required.", nameof(UserId));
            }

            if (string.IsNullOrWhiteSpace(Message))
            {
                throw new ArgumentException("Message is required.", nameof(Message));
            }

            var client = _lineMessagingClient ?? new LineMessagingClient(GetRequiredChannelAccessToken().Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase));
            var messages = new List<ISendMessage> { new TextMessage(Message) };
            await client.PushMessageAsync(UserId, messages, retryKey).ConfigureAwait(false);
        }
```

Important note: this method accepts `retryKey`, but it does not know or set `X-Line-Retry-Key`. The SDK owns the header.

- [ ] **Step 5: Run processor tests and verify GREEN**

Run:

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --no-restore -v minimal
```

Expected: all `LineMessagingProcessorReliableNotificationTests` pass.

### Task 3: Add Payment Notification Retry-Key Builder Tests

**Files:**
- Modify or create focused tests in the most appropriate existing project after inspecting references.

Use `ChurchReport.MemberInfo.Tests` only if it already references `ChurchReport`. If not, create `ChurchReport.PaymentNotification.Tests` using the same package versions as the existing test projects.

- [ ] **Step 1: Inspect test project references**

Run:

```powershell
Get-Content ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -Raw
Get-Content SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj -Raw
```

Expected: identify whether an existing test project can reference `ChurchReport/Services/PaymentNotificationService.cs` without introducing a large dependency problem.

- [ ] **Step 2: Add tests for deterministic retry-key construction**

If using a new project, create `ChurchReport.PaymentNotification.Tests/PaymentNotificationRetryKeyTests.cs` with this test shape:

```csharp
using ChurchReport.Services;
using FluentAssertions;

namespace ChurchReport.PaymentNotification.Tests;

public sealed class PaymentNotificationRetryKeyTests
{
    [Fact]
    public void BuildPaymentLineRetryKey_uses_order_id_when_available()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: "order-1001",
            productOrderId: "product-2002",
            status: "paid");

        key.Should().Be("churchreport:payment:order-1001:paid:payer-line-notice");
    }

    [Fact]
    public void BuildPaymentLineRetryKey_falls_back_to_product_order_id()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: " ",
            productOrderId: "product-2002",
            status: "paid");

        key.Should().Be("churchreport:payment:product-2002:paid:payer-line-notice");
    }

    [Fact]
    public void BuildPaymentLineRetryKey_returns_null_without_stable_identifier()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: null,
            productOrderId: " ",
            status: "paid");

        key.Should().BeNull();
    }

    [Fact]
    public void BuildPaymentLineRetryKey_does_not_include_sensitive_or_personal_data()
    {
        var key = PaymentNotificationService.BuildPaymentLineRetryKey(
            orderId: "order-1001",
            productOrderId: "product-2002",
            status: "paid");

        key.Should().NotContain("U1234567890abcdef");
        key.Should().NotContain("payer-name");
        key.Should().NotContain("card-token");
        key.Should().NotContain("payment received");
    }
}
```

- [ ] **Step 3: Run the retry-key tests and verify RED**

Run the chosen test project:

```powershell
dotnet test ChurchReport.PaymentNotification.Tests\ChurchReport.PaymentNotification.Tests.csproj -v minimal --filter PaymentNotificationRetryKeyTests
```

Expected: build fails because `PaymentNotificationService.BuildPaymentLineRetryKey(...)` does not exist yet.

### Task 4: Implement Payment Notification Reliable Send

**Files:**
- Modify: `ChurchReport/ChurchReport.csproj`
- Modify: `ChurchReport/Services/PaymentNotificationService.cs`

- [ ] **Step 1: Add project reference if needed**

If `ChurchReport/ChurchReport.csproj` does not already reference `LineMessagingProcessor`, add:

```xml
  <ItemGroup>
    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Add namespace import**

In `ChurchReport/Services/PaymentNotificationService.cs`, add:

```csharp
using LineMessagingProcessor;
```

- [ ] **Step 3: Add deterministic retry-key helper**

Inside `PaymentNotificationService`, add this public static helper near the constructor:

```csharp
        public static string? BuildPaymentLineRetryKey(string? orderId, string? productOrderId, string status)
        {
            var stableId = !string.IsNullOrWhiteSpace(orderId)
                ? orderId.Trim()
                : !string.IsNullOrWhiteSpace(productOrderId)
                    ? productOrderId.Trim()
                    : null;

            if (stableId == null)
            {
                return null;
            }

            var normalizedStatus = string.IsNullOrWhiteSpace(status)
                ? "unknown"
                : status.Trim().ToLowerInvariant();

            return $"churchreport:payment:{stableId}:{normalizedStatus}:payer-line-notice";
        }
```

- [ ] **Step 4: Add reliable send overload while preserving old method**

Keep the existing method signature and delegate to the new overload:

```csharp
        public void SendLineMessage(string lineId, string message)
        {
            SendLineMessage(lineId, message, retryKey: null);
        }
```

Add the overload:

```csharp
        public void SendLineMessage(string lineId, string message, string? retryKey)
        {
            try
            {
                var channelAccessToken = GetLineChannelAccessToken();

                if (string.IsNullOrWhiteSpace(retryKey))
                {
                    var lineMessagingClient = new LineMessagingClient(channelAccessToken);
                    var pushUtility = new PushUtility(lineMessagingClient);
                    pushUtility.SendMessage(lineId, message).Wait();
                }
                else
                {
                    var processor = new LineMessagingProcessorClass(channelAccessToken);
                    processor.SendReliableMessageAsync(lineId, message, retryKey).Wait();
                }

                _logger.LogInformation($"SendLineMessage: 已發送 - LineId: {lineId}, RetryKey: {retryKey ?? \"<none>\"}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SendLineMessage: 發送失敗 - LineId: {lineId}, RetryKey: {retryKey ?? \"<none>\"}");
            }
        }
```

This preserves the old non-retry path and uses the reliable adapter only when a deterministic retry key exists.

- [ ] **Step 5: Update the selected payment notification calls**

For payment-result notification paths in `PaymentNotificationService`, replace:

```csharp
SendLineMessage(lineId, message);
```

with a local retry key:

```csharp
var retryKey = BuildPaymentLineRetryKey(
    orderId: result?.OrderId,
    productOrderId: result?.ProductOrderId,
    status: result?.IsSuccess == true ? "paid" : "failed");
SendLineMessage(lineId, message, retryKey);
```

If the actual `PaymentWorkflowResult` uses different property names, inspect the class and use the stable order identifier already used in the notification message. Do not invent random IDs.

- [ ] **Step 6: Run payment notification tests and verify GREEN**

Run:

```powershell
dotnet test ChurchReport.PaymentNotification.Tests\ChurchReport.PaymentNotification.Tests.csproj --no-restore -v minimal --filter PaymentNotificationRetryKeyTests
```

Expected: retry-key builder tests pass.

### Task 5: Full Verification And Cleanup

**Files:**
- No new production files unless previous tasks required a test project.

- [ ] **Step 1: Run processor tests**

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --no-restore -v minimal
```

Expected: all processor adapter tests pass.

- [ ] **Step 2: Run LINE SDK tests**

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal
```

Expected: all LINE SDK tests pass.

- [ ] **Step 3: Run payment-related tests**

Run the selected payment notification test project:

```powershell
dotnet test ChurchReport.PaymentNotification.Tests\ChurchReport.PaymentNotification.Tests.csproj --no-restore -v minimal
```

Expected: all tests pass.

- [ ] **Step 4: Run solution build**

```powershell
dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds with 0 errors. Existing unrelated warnings may remain.

- [ ] **Step 5: Verify scope**

```powershell
git diff --name-only
```

Expected changed files are limited to the processor adapter, payment notification service, test project/files, solution/project references, and the task/plan files. The unrelated `.ccg/tasks/qpay-model-boundary-brainstorm/.turns.json` must not be staged.

- [ ] **Step 6: Verify generated outputs are absent**

```powershell
$root=(Resolve-Path -LiteralPath '.').Path
$generated=Get-ChildItem -Path $root -Recurse -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @('bin','obj','artifacts') }
$generated.Count
```

Expected: `0` after cleanup.

If not zero, clean with:

```powershell
$generated | Sort-Object FullName -Descending | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
```

- [ ] **Step 7: Commit implementation**

```powershell
git add -- LineMessagingProcessor/LineMessagingProcessor.csproj LineMessagingProcessor/LineMessagingProcessorClass.cs LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj LineMessagingProcessor.Tests/LineMessagingProcessorReliableNotificationTests.cs ChurchReport/ChurchReport.csproj ChurchReport/Services/PaymentNotificationService.cs ChurchReport.sln
git commit -m "feat: add reliable LINE payment notifications"
```

Expected: one focused implementation commit. Do not stage `.ccg/tasks/qpay-model-boundary-brainstorm/.turns.json`.

### Task 6: Review Gate

**Files:**
- Create: `.ccg/tasks/line-reliable-notification-adapter/review.md`

- [ ] **Step 1: Run Gemini review**

Use the reviewer role with Gemini to review `git diff HEAD~1..HEAD` for:

- retry-key correctness
- SDK/processor/ChurchReport boundaries
- test adequacy
- payment callback behavior risk
- scope creep

- [ ] **Step 2: Run Claude review if available**

Run Claude reviewer with the same scope. If Claude CLI quota is unavailable and the user again waives it, record the waiver explicitly in `review.md`.

- [ ] **Step 3: Write review record**

Create `.ccg/tasks/line-reliable-notification-adapter/review.md` with:

```markdown
# LINE Reliable Notification Adapter Review

## Verification

- Processor tests: <actual result>
- LINE SDK tests: <actual result>
- Payment notification tests: <actual result>
- Solution build: <actual result>
- Generated outputs: <actual result>

## Gemini Review

<actual review text or summary>

## Claude Review

<actual review text, or explicit user-approved waiver if quota unavailable>

## Lead Disposition

- Critical: <actual disposition>
- Warning: <actual disposition>
- Info: <actual disposition>
```

- [ ] **Step 4: Fix valid Critical findings**

If any Critical finding is valid, fix it, rerun tests/build, and rerun review.

- [ ] **Step 5: Commit review record**

```powershell
git add -- .ccg/tasks/line-reliable-notification-adapter/review.md
git commit -m "chore: record LINE reliable notification adapter review"
```

## Self-Review Checklist

- [ ] Plan implements the approved A+B design without entering P2.
- [ ] `Line.Messaging` remains the only layer that applies `X-Line-Retry-Key`.
- [ ] `LineMessagingProcessor` stays product-neutral.
- [ ] ChurchReport owns deterministic payment retry-key generation.
- [ ] Old send methods remain compatible.
- [ ] Tests prove retry-key propagation and deterministic key construction.
- [ ] No broad migration of all LINE call sites is included.
- [ ] No unrelated `.ccg` task files are staged.
