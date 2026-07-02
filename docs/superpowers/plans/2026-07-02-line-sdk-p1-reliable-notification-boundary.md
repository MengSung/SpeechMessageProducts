# LINE SDK P1 Reliable Notification Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This repository is in inline mode, so do not dispatch implement/check subagents.

**Goal:** Add official LINE `X-Line-Retry-Key` support to push, multicast, and broadcast message APIs while preserving existing ChurchReport behavior and keeping LINE protocol details inside `Line.Messaging`.

**Architecture:** `Line.Messaging` owns endpoint, payload, and retry-key header construction. Existing public methods remain compatible and delegate to new overloads that accept a retry key. `LineMessagingProcessor` and ChurchReport stay product-facing adapters and do not manually construct LINE protocol headers in this P1 slice.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, Newtonsoft.Json, existing `Line.Messaging`, `LineMessagingProcessor`, and `Line.Messaging.Tests` projects.

---

## Scope And Order

This plan implements only the approved P1 first-round scope from `docs/superpowers/specs/2026-07-02-line-sdk-p1-reliable-notification-boundary-design.md`.

Included:

1. Add `X-Line-Retry-Key` support for typed push, multicast, and broadcast message APIs.
2. Preserve all existing overloads and behavior when no retry key is supplied.
3. Centralize retry-key header behavior inside one SDK helper.
4. Add request-capturing tests for retry header presence/absence, endpoint stability, and body stability.
5. Document the processor boundary without moving ChurchReport business logic.

Excluded:

- Do not implement P2 items.
- Do not touch `LinePayCSharp/`.
- Do not implement Audience or Narrowcast APIs.
- Do not refactor CRM, payment, donation, or LINE webhook flows.
- Do not introduce ChurchReport, CRM, or DbContext dependencies into `Line.Messaging/`.

## Files And Responsibilities

- Modify `Line.Messaging/ILineMessagingClient.cs`: add retry-key overload declarations for typed push, multicast, and broadcast methods while keeping existing declarations unchanged.
- Modify `Line.Messaging/LineMessagingClient.cs`: add `ApplyRetryKeyHeader`, delegate old typed overloads to retry-key overloads, and use `ApiUrl(...)` for touched message endpoints.
- Create `Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs`: focused request-capturing tests for retry header behavior.
- Modify `.ccg/tasks/line-messaging-sdk-p1-brainstorm/task.json`: track planning and implementation state.

## API Design

Add these interface and client overloads:

```csharp
Task PushMessageAsync(string to, IList<ISendMessage> messages, string retryKey);
Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages, string retryKey);
Task BroadcastMessageAsync(IList<ISendMessage> messages, string retryKey);
```

Existing overloads stay source-compatible and delegate:

```csharp
public virtual Task PushMessageAsync(string to, IList<ISendMessage> messages)
    => PushMessageAsync(to, messages, retryKey: null);
```

Central helper:

```csharp
private static void ApplyRetryKeyHeader(HttpRequestMessage request, string retryKey)
{
    if (request == null)
    {
        throw new ArgumentNullException(nameof(request));
    }

    if (string.IsNullOrWhiteSpace(retryKey))
    {
        return;
    }

    request.Headers.TryAddWithoutValidation("X-Line-Retry-Key", retryKey);
}
```

---

### Task 1: Add Failing Retry-Key Tests

**Files:**
- Create: `Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs`

- [ ] **Step 1: Create the test file with request capture helper**

Create `Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs` with this content:

```csharp
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessagingClientP1RetryKeyTests
{
    [Fact]
    public async Task Push_message_with_retry_key_sends_line_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") },
            "fee-1001-notification");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("fee-1001-notification");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
        body["messages"]!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Push_message_existing_overload_does_not_send_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") });

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    [Fact]
    public async Task Multicast_message_with_retry_key_sends_line_retry_header_and_keeps_body()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.MultiCastMessageAsync(
            new List<string> { "U111", "U222" },
            new List<ISendMessage> { new TextMessage("batch notice") },
            "batch-20260702-001");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/multicast");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("batch-20260702-001");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Select(token => token.Value<string>()).Should().Equal("U111", "U222");
        body["messages"]!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Broadcast_message_with_retry_key_sends_line_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.BroadcastMessageAsync(
            new List<ISendMessage> { new TextMessage("global notice") },
            "broadcast-20260702-001");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/broadcast");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("broadcast-20260702-001");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_retry_key_does_not_send_retry_header(string retryKey)
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") },
            retryKey);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    private static LineMessagingClient CreateClient(CapturingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly string _mediaType;

        public CapturingHttpMessageHandler(string responseBody = "{}", HttpStatusCode statusCode = HttpStatusCode.OK, string mediaType = "application/json")
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _mediaType = mediaType;
        }

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, _mediaType)
            };
        }
    }
}
```

- [ ] **Step 2: Run focused tests and verify they fail before implementation**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal --filter LineMessagingClientP1RetryKeyTests
```

Expected: build fails because retry-key overloads do not exist yet.

### Task 2: Add Retry-Key Overloads To Interface

**Files:**
- Modify: `Line.Messaging/ILineMessagingClient.cs`

- [ ] **Step 1: Add interface overloads**

Add these declarations near the existing push, multicast, and broadcast declarations:

```csharp
Task PushMessageAsync(string to, IList<ISendMessage> messages, string retryKey);
Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages, string retryKey);
Task BroadcastMessageAsync(IList<ISendMessage> messages, string retryKey);
```

- [ ] **Step 2: Build and verify implementation is still missing**

Run:

```powershell
dotnet build Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal
```

Expected: build fails because `LineMessagingClient` does not implement the new interface members yet.

### Task 3: Implement Retry-Key Support In Client

**Files:**
- Modify: `Line.Messaging/LineMessagingClient.cs`

- [ ] **Step 1: Add the retry-key helper**

Add the `ApplyRetryKeyHeader(HttpRequestMessage request, string retryKey)` helper near the existing URL helpers.

- [ ] **Step 2: Implement push overload delegation**

Replace typed push with a delegating old overload and a retry-key overload that uses `ApiUrl("/bot/message/push")`, calls `ApplyRetryKeyHeader`, then sends the existing JSON body.

- [ ] **Step 3: Implement multicast overload delegation**

Replace typed multicast with a delegating old overload and a retry-key overload that uses `ApiUrl("/bot/message/multicast")`, calls `ApplyRetryKeyHeader`, then sends the existing JSON body.

- [ ] **Step 4: Implement broadcast overload delegation**

Replace typed broadcast with a delegating old overload and a retry-key overload that uses `ApiUrl("/bot/message/broadcast")`, calls `ApplyRetryKeyHeader`, then sends the existing JSON body.

- [ ] **Step 5: Run focused retry tests**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal --filter LineMessagingClientP1RetryKeyTests
```

Expected: all P1 retry-key tests pass.

### Task 4: Full Verification And Boundary Check

**Files:**
- Modify: `.ccg/tasks/line-messaging-sdk-p1-brainstorm/task.json`

- [ ] **Step 1: Run all LINE Messaging tests**

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj --no-restore -v minimal
```

Expected: all tests pass.

- [ ] **Step 2: Run solution build**

```powershell
dotnet build ChurchReport.sln --no-restore -m:1 -v minimal -p:UseSharedCompilation=false
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Verify no forbidden scope changes**

```powershell
git diff --name-only
```

Expected changed files are limited to `Line.Messaging/ILineMessagingClient.cs`, `Line.Messaging/LineMessagingClient.cs`, `Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs`, and `.ccg/tasks/line-messaging-sdk-p1-brainstorm/task.json`.

- [ ] **Step 4: Verify retry header is centralized**

```powershell
Select-String -Path 'Line.Messaging\*.cs','Line.Messaging\**\*.cs','Line.Messaging.Tests\*.cs' -Pattern 'X-Line-Retry-Key' -AllMatches
```

Expected: matches only in `LineMessagingClient.cs` and `LineMessagingClientP1RetryKeyTests.cs`.

- [ ] **Step 5: Clean build outputs**

```powershell
$root=(Resolve-Path -LiteralPath '.').Path
$targets=Get-ChildItem -Path $root -Recurse -Force -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @('bin','obj','artifacts') }
$targets | Sort-Object FullName -Descending | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
```

Expected: no `bin/`, `obj/`, or `artifacts/` directories remain.

- [ ] **Step 6: Commit implementation changes**

```powershell
git add -- Line.Messaging/ILineMessagingClient.cs Line.Messaging/LineMessagingClient.cs Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs .ccg/tasks/line-messaging-sdk-p1-brainstorm/task.json
git commit -m "feat: add LINE retry key support for reliable notifications"
```

Expected: one implementation commit containing only P1 retry-key scope.

### Task 5: External Review Gate

**Files:**
- Create: `.ccg/tasks/line-messaging-sdk-p1-brainstorm/review.md`

- [ ] **Step 1: Run Gemini reviewer**

Use `codeagent-wrapper --backend gemini` with reviewer role to review `git diff HEAD~1..HEAD` for official retry-key behavior, API compatibility, SDK/product boundary, test adequacy, and scope creep.

- [ ] **Step 2: Run Claude reviewer**

Use `codeagent-wrapper --backend claude` with reviewer role to review `git diff HEAD~1..HEAD` for the same scope.

- [ ] **Step 3: Write review summary**

Create `.ccg/tasks/line-messaging-sdk-p1-brainstorm/review.md` with actual Gemini/Claude Critical/Warning/Info findings, dispositions, verification commands, and cleanup result. Do not leave blank fields.

- [ ] **Step 4: Fix valid Critical findings**

For every Critical finding, verify against code and official LINE behavior, fix, rerun focused tests and solution build, then rerun both Gemini and Claude review.

- [ ] **Step 5: Commit review record**

```powershell
git add -- .ccg/tasks/line-messaging-sdk-p1-brainstorm/review.md .ccg/tasks/line-messaging-sdk-p1-brainstorm/review-gemini.txt .ccg/tasks/line-messaging-sdk-p1-brainstorm/review-claude.txt
git commit -m "chore: record LINE SDK P1 external review"
```

## Self-Review Checklist

- [ ] Every approved spec requirement maps to a task above.
- [ ] No P2, Audience, Narrowcast, LinePayCSharp, CRM, payment, donation, or webhook implementation is included.
- [ ] Existing public API remains source-compatible.
- [ ] Retry-key behavior is centralized in one SDK helper.
- [ ] Tests prove header presence, absence, endpoint stability, and body stability.
- [ ] Verification includes test, build, scope diff, header centralization search, and build-output cleanup.
- [ ] External review requires both Gemini and Claude.