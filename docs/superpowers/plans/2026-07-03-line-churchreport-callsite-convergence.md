# ChurchReport LINE Call Site Convergence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for inline execution. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move ChurchReport payment-priority LINE notification call sites toward `ILineNotificationWorkflow` without moving CRM, payment, donation, or MVC logic into shared LINE projects.

**Architecture:** Keep shared LINE message construction and send workflow inside `LineMessagingProcessor.Workflows`; keep ChurchReport-specific message wording and CRM/payment decisions inside ChurchReport. The first implementation slice upgrades `PushUtility` as the compatibility shim, then routes required payment notifications through workflow-backed throwing methods.

**Tech Stack:** C#/.NET 10, xUnit, FluentAssertions, `LineMessagingProcessor.Workflows`, ChurchReport product services.

---

## File Map

- Modify: `ChurchReport/Tools/PushUtility.cs`
  - Responsibility: ChurchReport legacy push shim. It should prefer `ILineNotificationWorkflow` when injected and keep legacy behavior where required.
- Test: `ChurchReport.MemberInfo.Tests` or a new focused ChurchReport test project/file if a suitable test project already references ChurchReport tools.
  - Responsibility: prove `PushUtility` workflow routing and throw/swallow behavior without calling real LINE API.
- Modify: selected payment call sites only after `PushUtility` behavior is proven:
  - `ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs`
  - `ChurchReport/Tools/DonationFeePaymentProcessor.cs`
  - `ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- Do not modify:
  - `LineMessagingProcessor.Workflows` unless a missing neutral capability is proven by tests.
  - `SpeechMessage.Payments`.
  - CRM/payment business logic.

---

### Task 1: Add Tests For Workflow-Backed PushUtility Semantics

**Files:**
- Test: `ChurchReport.MemberInfo.Tests/PushUtilityWorkflowTests.cs`
- Modify only if needed: `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`

- [ ] **Step 1: Inspect existing test project references**

Run:

```powershell
dotnet list ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj reference
```

Expected: confirm whether the test project already references `ChurchReport`, `LineMessagingProcessor.Workflows`, and `Line.Messaging`.

- [ ] **Step 2: Add failing tests**

Create `ChurchReport.MemberInfo.Tests/PushUtilityWorkflowTests.cs` with tests for these behaviors:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public sealed class PushUtilityWorkflowTests
{
    [Fact]
    public async Task SendMessage_uses_workflow_when_available_and_remains_non_throwing()
    {
        var workflow = new CapturingWorkflow();
        var utility = new PushUtility(CreateUnusedClient(), workflow);

        await utility.SendMessage("U123", "hello");

        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Recipient.UserId.Should().Be("U123");
        workflow.Requests[0].Content.Text.Should().Be("hello");
    }

    [Fact]
    public async Task SendMessage_swallows_workflow_failure_for_legacy_best_effort_behavior()
    {
        var workflow = new CapturingWorkflow
        {
            SendAsyncResult = LineNotificationResult.ProviderRejected("line-error", "LINE rejected")
        };
        var utility = new PushUtility(CreateUnusedClient(), workflow);

        var action = () => utility.SendMessage("U123", "hello");

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendMessageOrThrowAsync_uses_workflow_and_propagates_failure()
    {
        var workflow = new CapturingWorkflow
        {
            SendOrThrowException = new LineNotificationException(
                LineNotificationResult.ProviderRejected("line-error", "LINE rejected"))
        };
        var utility = new PushUtility(CreateUnusedClient(), workflow);

        var action = () => utility.SendMessageOrThrowAsync("U123", "required");

        await action.Should().ThrowAsync<LineNotificationException>();
        workflow.Requests.Should().ContainSingle();
        workflow.Requests[0].Content.Text.Should().Be("required");
    }

    private static LineMessagingClient CreateUnusedClient()
        => new(new HttpClient(new ThrowingHandler()), "unused-token", "https://api.line.me/v2");

    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        public List<LineNotificationRequest> Requests { get; } = new();

        public LineNotificationResult SendAsyncResult { get; set; } = LineNotificationResult.Success();

        public Exception? SendOrThrowException { get; set; }

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(SendAsyncResult);
        }

        public Task SendOrThrowAsync(LineNotificationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (SendOrThrowException != null)
            {
                throw SendOrThrowException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The test should use ILineNotificationWorkflow, not real HTTP.");
    }
}
```

- [ ] **Step 3: Run RED tests**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter PushUtilityWorkflowTests
```

Expected: at least `SendMessageOrThrowAsync_uses_workflow_and_propagates_failure` fails because `SendMessageOrThrowAsync` currently bypasses `_lineNotificationWorkflow`.

---

### Task 2: Route PushUtility Required Text Sends Through Workflow

**Files:**
- Modify: `ChurchReport/Tools/PushUtility.cs`

- [ ] **Step 1: Implement minimal workflow path in `SendMessageOrThrowAsync`**

Change `SendMessageOrThrowAsync(string UserId, string Message)` to:

```csharp
public async Task SendMessageOrThrowAsync(string UserId, string Message)
{
    if (string.IsNullOrWhiteSpace(UserId))
    {
        throw new ArgumentException("LINE user id is required.", nameof(UserId));
    }

    if (_lineNotificationWorkflow != null)
    {
        await _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(UserId),
            Content = LineNotificationContent.TextMessage(Message),
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "ChurchReport.PushUtility.RequiredText"
            }
        });
        return;
    }

    List<ISendMessage> MessageToSend = new List<ISendMessage>
    {
        new TextMessage(Message)
    };

    await this.m_LineMessagingClient.PushMessageAsync(UserId, MessageToSend);
}
```

- [ ] **Step 2: Verify PushUtility tests pass**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter PushUtilityWorkflowTests
```

Expected: all `PushUtilityWorkflowTests` pass.

- [ ] **Step 3: Verify existing workflow tests still pass**

Run:

```powershell
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
```

Expected: pass.

---

### Task 3: Add Required SDK-Message Throwing Path For Non-Text Payment Notices

**Files:**
- Modify: `ChurchReport/Tools/PushUtility.cs`
- Modify: `ChurchReport.MemberInfo.Tests/PushUtilityWorkflowTests.cs`

- [ ] **Step 1: Add failing test for required SDK message sends**

Append this test:

```csharp
[Fact]
public async Task SendMessagesOrThrowAsync_uses_workflow_escape_hatch_for_required_sdk_messages()
{
    var workflow = new CapturingWorkflow();
    var utility = new PushUtility(CreateUnusedClient(), workflow);
    var messages = new List<ISendMessage> { new TextMessage("sdk") };

    await utility.SendMessagesOrThrowAsync("U123", messages);

    workflow.Requests.Should().ContainSingle();
    workflow.Requests[0].Content.SdkMessages.Should().BeSameAs(messages);
}
```

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter SendMessagesOrThrowAsync
```

Expected: compile fails because `SendMessagesOrThrowAsync` does not exist.

- [ ] **Step 2: Implement `SendMessagesOrThrowAsync`**

Add to `PushUtility`:

```csharp
public async Task SendMessagesOrThrowAsync(string UserId, IReadOnlyList<ISendMessage> messages)
{
    if (string.IsNullOrWhiteSpace(UserId))
    {
        throw new ArgumentException("LINE user id is required.", nameof(UserId));
    }

    if (_lineNotificationWorkflow != null)
    {
        await _lineNotificationWorkflow.SendOrThrowAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(UserId),
            Content = LineNotificationContent.SdkMessagesList(messages),
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "ChurchReport.PushUtility.RequiredSdkMessages"
            }
        });
        return;
    }

    await this.m_LineMessagingClient.PushMessageAsync(UserId, messages);
}
```

- [ ] **Step 3: Verify tests pass**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter PushUtilityWorkflowTests
```

Expected: pass.

---

### Task 4: Route Payment Call Sites Without Moving Product Logic

**Files:**
- Inspect/modify only if constructor injection path is clear:
  - `ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs`
  - `ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs`
  - `ChurchReport/Tools/DonationFeePaymentProcessor.cs`
  - `ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`

- [ ] **Step 1: Inspect constructors and existing dependency injection**

Run:

```powershell
rg -n "DonationPaymentProcessor\\(|DonationFeePaymentProcessor\\(|RecurringDonationPaymentProcessor\\(|new PushUtility|ILineNotificationWorkflow" ChurchReport --glob "*.cs"
```

Expected: identify constructors that already accept `PushUtility` or can accept `ILineNotificationWorkflow` without broad controller rewrites.

- [ ] **Step 2: Prefer passing workflow-backed `PushUtility`**

Where a product class already accepts `PushUtility`, ensure the caller passes a `PushUtility` constructed with `ILineNotificationWorkflow`.

Do not change CRM update logic, fee creation logic, payment provider logic, or MVC result logic.

- [ ] **Step 3: Upgrade only required payment notices first**

Replace only required payment notification sends with throwing methods:

```csharp
await PushUtility.SendMessageOrThrowAsync(lineId, lineMessage);
```

or, if sending SDK message lists:

```csharp
await PushUtility.SendMessagesOrThrowAsync(lineId, messages);
```

Do not change best-effort gratitude or admin/debug notifications in this task unless tests already cover the intended behavior.

- [ ] **Step 4: Verify no shared LINE project references ChurchReport**

Run:

```powershell
rg -n "ChurchReport|Microsoft\\.Xrm|Controller|IActionResult|DbContext" LineMessagingProcessor LineMessagingProcessor.Workflows LineMessagingProcessor.AspNetCore --glob "*.cs" --glob "*.csproj"
```

Expected: no product dependency introduced into shared LINE workflow projects. ASP.NET Core project may contain framework references, but not ChurchReport product references.

---

### Task 5: Validation, Review, And Commit

**Files:**
- Modify: `.ccg/tasks/line-churchreport-callsite-convergence/task.json`
- Create: `.ccg/tasks/line-churchreport-callsite-convergence/review.md`
- Archive: `.ccg/tasks/archive/2026-07/line-churchreport-callsite-convergence/`

- [ ] **Step 1: Run targeted and solution validation**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false --filter PushUtilityWorkflowTests
dotnet test LineMessagingProcessor.Workflows.Tests\LineMessagingProcessor.Workflows.Tests.csproj -v minimal -m:1 -p:UseSharedCompilation=false
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

- [ ] **Step 2: Run dual-model CCG review**

Review `git diff` with both Gemini and Claude reviewer roles. If Claude fails due tooling/quota and Gemini plus local validation pass, record Claude as non-blocking per prior user instruction.

- [ ] **Step 3: Normalize touched files**

All touched `.cs`, `.md`, and `.json` files must be UTF-8 without BOM and CRLF.

- [ ] **Step 4: Archive task and commit**

```powershell
git add -- ChurchReport/Tools/PushUtility.cs ChurchReport.MemberInfo.Tests docs/superpowers .ccg/tasks
git commit -m "refactor: route ChurchReport LINE push notices through workflow"
```

---

## Self-Review

Spec coverage:

- Required payment/notification paths are prioritized by Tasks 2-4.
- Best-effort notification behavior is preserved by Task 1 and Task 2.
- ChurchReport CRM/payment/donation logic remains in ChurchReport by boundary rules and Task 4 constraints.
- Shared LINE projects do not receive ChurchReport dependencies.

Placeholder scan:

- No TBD/TODO placeholders are used.
- Each implementation step has exact files, commands, and expected results.

Type consistency:

- `ILineNotificationWorkflow`, `LineNotificationRequest`, `LineNotificationRecipient`, `LineNotificationContent`, and `LineNotificationException` match the existing workflow API.
- The new `SendMessagesOrThrowAsync` name is intentionally plural to distinguish SDK message-list sending from text sending.

