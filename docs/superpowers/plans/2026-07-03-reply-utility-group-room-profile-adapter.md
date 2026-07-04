# ReplyUtility Group Room Profile Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route `ReplyUtility.EchoAsyncProcessor(...)` group and room profile lookup through `LineMessagingProcessorClass` instead of direct `LineMessagingClient` calls.

**Architecture:** `ReplyUtility` remains responsible for ChurchReport reply workflow and text composition. `LineMessagingProcessorClass` owns reusable group/room member profile lookup validation and delegates to the SDK. Reply sending stays on the existing `LineMessagingClient` path in this slice.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, Newtonsoft.Json.Linq, `Line.Messaging`, `LineMessagingProcessor`, ChurchReport `ReplyUtility`.

---

## File Map

- Modify: `ChurchReport/Tools/ReplyUtility.cs`
  - Add a processor dependency.
  - Keep existing public constructor compatible.
  - Add an internal constructor for tests.
  - Change only group/room profile lookup calls inside `EchoAsyncProcessor(...)`.
- Create: `ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs`
  - Test group source profile lookup request and reply text.
  - Test room source profile lookup request and reply text.
  - Test direct user source does not call group/room profile endpoints.
- Modify: `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`
  - Add project references if needed for `ChurchReport`, `Line.Messaging`, or `LineMessagingProcessor`.

## Guardrails

- Do not modify `PushUtility`.
- Do not modify rich menu methods.
- Do not modify LIFF views or JavaScript.
- Do not add broad LINE P2 official API coverage.
- Do not move ChurchReport CRM, controller, payment, or UI behavior into `Line.Messaging` or `LineMessagingProcessor`.
- Do not change reply sending behavior in `ReplyUtility`.

## Task 1: Add ReplyUtility Routing Tests

**Files:**
- Create: `ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs`
- Inspect/modify only if compilation requires it: `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`

- [ ] **Step 1: Inspect test project references**

Run:

```powershell
dotnet list ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj reference
```

Expected: identify whether the test project already references `ChurchReport`, `Line.Messaging`, and `LineMessagingProcessor`. If a reference is missing, add it with `dotnet add ... reference ...`.

- [ ] **Step 2: Create the failing test file**

Create `ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs` with this content:

```csharp
using System.Net;
using System.Text;
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using Line.Messaging.Webhooks;
using LineMessagingProcessor;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public sealed class ReplyUtilityGroupRoomProfileAdapterTests
{
    [Fact]
    public async Task EchoAsyncProcessor_group_source_gets_profile_through_processor_and_replies_with_display_name()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Group User","userId":"Ugroup","pictureUrl":"https://example.com/group.png","statusMessage":"group"}""");
        using var httpClient = new HttpClient(handler);
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor);
        var ev = CreateTextEvent(EventSourceType.Group, "G123", "Ugroup", "reply-token", "hello");

        await utility.EchoAsyncProcessor(ev);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/group/G123/member/Ugroup");
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/reply");

        var replyBody = JObject.Parse(handler.Bodies[1]);
        replyBody["replyToken"]!.Value<string>().Should().Be("reply-token");
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("Group User");
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("hello");
    }

    [Fact]
    public async Task EchoAsyncProcessor_room_source_gets_profile_through_processor_and_replies_with_display_name()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Room User","userId":"Uroom","pictureUrl":"https://example.com/room.png","statusMessage":"room"}""");
        using var httpClient = new HttpClient(handler);
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor);
        var ev = CreateTextEvent(EventSourceType.Room, "R123", "Uroom", "reply-token", "hello");

        await utility.EchoAsyncProcessor(ev);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/room/R123/member/Uroom");
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/reply");

        var replyBody = JObject.Parse(handler.Bodies[1]);
        replyBody["replyToken"]!.Value<string>().Should().Be("reply-token");
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("Room User");
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("hello");
    }

    [Fact]
    public async Task EchoAsyncProcessor_user_source_replies_without_group_or_room_profile_lookup()
    {
        var handler = new CapturingHttpMessageHandler("{}");
        using var httpClient = new HttpClient(handler);
        var lineClient = new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(lineClient);
        var utility = new ReplyUtility(lineClient, processor);
        var ev = CreateTextEvent(EventSourceType.User, "Udirect", "Udirect", "reply-token", "hello");

        await utility.EchoAsyncProcessor(ev);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/reply");

        var replyBody = JObject.Parse(handler.Bodies[0]);
        replyBody["messages"]![0]!["text"]!.Value<string>().Should().Contain("hello");
    }

    private static MessageEvent CreateTextEvent(
        EventSourceType sourceType,
        string sourceId,
        string userId,
        string replyToken,
        string text)
    {
        return new MessageEvent
        {
            ReplyToken = replyToken,
            Source = new EventSource
            {
                Type = sourceType,
                Id = sourceId,
                UserId = userId
            },
            Message = new TextEventMessage
            {
                Text = text
            }
        };
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public CapturingHttpMessageHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses.Length == 0 ? new[] { "{}" } : responses);
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var json = _responses.Count > 0 ? _responses.Dequeue() : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
```

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter ReplyUtilityGroupRoomProfileAdapterTests -v minimal
```

Expected: fail to compile because `ReplyUtility(LineMessagingClient, LineMessagingProcessorClass)` does not exist yet.

## Task 2: Add Processor Dependency To ReplyUtility

**Files:**
- Modify: `ChurchReport/Tools/ReplyUtility.cs`

- [ ] **Step 1: Add the processor using**

Add this using at the top:

```csharp
using LineMessagingProcessor;
```

- [ ] **Step 2: Add a processor field and constructor overload**

Change the initialization region to this shape:

```csharp
        private LineMessagingClient m_LineMessagingClient { get; }

        private LineMessagingProcessorClass m_LineMessagingProcessor { get; }

        //private PushUtility m_PushUtility { get; }

        public ReplyUtility(LineMessagingClient LineMessagingClient)
            : this(LineMessagingClient, new LineMessagingProcessorClass(LineMessagingClient))
        {
        }

        internal ReplyUtility(
            LineMessagingClient LineMessagingClient,
            LineMessagingProcessorClass LineMessagingProcessor)
        {
            this.m_LineMessagingClient = LineMessagingClient ?? throw new ArgumentNullException(nameof(LineMessagingClient));
            this.m_LineMessagingProcessor = LineMessagingProcessor ?? throw new ArgumentNullException(nameof(LineMessagingProcessor));

            //m_PushUtility = new PushUtility(LineMessagingClient);
        }
```

- [ ] **Step 3: Route group and room lookups through the processor**

Replace this group lookup:

```csharp
var userProfile = await m_LineMessagingClient.GetGroupMemberProfileAsync(ev.Source.Id, ev.Source.UserId);
```

with:

```csharp
var userProfile = await m_LineMessagingProcessor.GetGroupMemberProfileAsync(ev.Source.Id, ev.Source.UserId);
```

Replace this room lookup:

```csharp
var userProfile = await m_LineMessagingClient.GetRoomMemberProfileAsync(ev.Source.Id, ev.Source.UserId);
```

with:

```csharp
var userProfile = await m_LineMessagingProcessor.GetRoomMemberProfileAsync(ev.Source.Id, ev.Source.UserId);
```

- [ ] **Step 4: Run focused tests and verify GREEN**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter ReplyUtilityGroupRoomProfileAdapterTests -v minimal
```

Expected: all `ReplyUtilityGroupRoomProfileAdapterTests` pass.

## Task 3: Validation And Review

**Files:**
- Modify: `.ccg/tasks/reply-utility-group-room-profile-adapter/task.json`
- Create: `.ccg/tasks/reply-utility-group-room-profile-adapter/review.md`

- [ ] **Step 1: Run full relevant tests**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -v minimal
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

Expected: all tests pass and build completes with 0 errors.

- [ ] **Step 2: Run boundary scans**

Run:

```powershell
rg -n "Microsoft\\.Xrm|CRM|Controller|IActionResult|DbContext" LineMessagingProcessor --glob "*.cs" --glob "*.csproj"
rg -n "GetGroupMemberProfileAsync|GetRoomMemberProfileAsync" ChurchReport\\Tools\\ReplyUtility.cs LineMessagingProcessor\\LineMessagingProcessorClass.cs --glob "*.cs"
```

Expected: first command has no hits in `LineMessagingProcessor`; second command shows `ReplyUtility` calling processor methods and processor calling SDK methods.

- [ ] **Step 3: Check touched text encoding**

Run:

```powershell
python -c "from pathlib import Path; files=[Path('ChurchReport/Tools/ReplyUtility.cs'),Path('ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs'),Path('docs/superpowers/plans/2026-07-03-reply-utility-group-room-profile-adapter.md'),Path('.ccg/tasks/reply-utility-group-room-profile-adapter/task.json'),Path('.ccg/tasks/reply-utility-group-room-profile-adapter/requirements.md')]; failed=False
for path in files:
    data=path.read_bytes(); bom=data.startswith(b'\\xef\\xbb\\xbf')
    try: data.decode('utf-8'); utf8=True
    except UnicodeDecodeError: utf8=False
    lf_only=any(data[i:i+1]==b'\\n' and (i==0 or data[i-1:i]!=b'\\r') for i in range(len(data)))
    print(f'{path}: utf8={utf8} bom={bom} lf_only={lf_only}')
    failed = failed or bom or (not utf8) or lf_only
raise SystemExit(1 if failed else 0)"
```

Expected: every file reports `utf8=True bom=False lf_only=False`.

- [ ] **Step 4: Run Gemini and Claude review**

Review the diff with both backends:

```powershell
$repo = (Get-Location).Path
$task = @'
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
Review the ReplyUtility group/room profile adapter diff.
Check correctness, boundary cleanliness, tests, encoding, and regressions.
Output Critical/Warning/Info findings.
</TASK>
OUTPUT: Critical/Warning/Info review report
'@
$env:GEMINI_CLI_TRUST_WORKSPACE='true'
$task | & "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --lite --backend gemini - $repo
```

Then run the same task with the Claude role:

```powershell
$repo = (Get-Location).Path
$task = @'
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
Review the ReplyUtility group/room profile adapter diff.
Check correctness, boundary cleanliness, tests, encoding, and regressions.
Output Critical/Warning/Info findings.
</TASK>
OUTPUT: Critical/Warning/Info review report
'@
$task | & "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --lite --backend claude - $repo
```

Write the results to `.ccg/tasks/reply-utility-group-room-profile-adapter/review.md`.

- [ ] **Step 5: Update task state**

Set `.ccg/tasks/reply-utility-group-room-profile-adapter/task.json` to:

```json
{
  "id": "reply-utility-group-room-profile-adapter",
  "title": "Route ReplyUtility group and room profile lookups through LineMessagingProcessor",
  "status": "completed",
  "complexity": "M",
  "risk": "medium",
  "domain": "backend",
  "currentPhase": "completed",
  "nextAction": "Archive after successful verification and review.",
  "createdAt": "2026-07-03T08:35:00+08:00",
  "branch": "Jesus_5.1.6.WorktreeRefactorLine",
  "completedAt": "2026-07-03T00:00:00+08:00"
}
```

- [ ] **Step 6: Clean generated outputs**

Run:

```powershell
Get-ChildItem -Recurse -Directory -Include bin,obj,artifacts |
    Where-Object { $_.FullName -notmatch '\\.git(\\|$)' } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
```

If a DLL is locked by a running dev server, record the locked path in the final report and continue only if `git status` is clean except intended tracked files.

- [ ] **Step 7: Commit**

Run:

```powershell
git add ChurchReport\Tools\ReplyUtility.cs ChurchReport.MemberInfo.Tests\ReplyUtilityGroupRoomProfileAdapterTests.cs .ccg\tasks\reply-utility-group-room-profile-adapter docs\superpowers\plans\2026-07-03-reply-utility-group-room-profile-adapter.md
git commit -m "feat: route ReplyUtility profile lookup through LINE processor"
```

Expected: commit succeeds.

## Self-Review Checklist

- Spec coverage: all requirements map to Task 1, Task 2, and Task 3.
- Scope control: no PushUtility, rich menu, LIFF, CRM, controller, or payment flow work is included.
- TDD: tests are written and run before production code changes.
- Boundary: processor remains reusable and ChurchReport product workflow stays in `ReplyUtility`.
- Encoding: touched files are explicitly checked as UTF-8 without BOM and CRLF.
