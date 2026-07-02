# LINE SDK P1 External Review

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine
Reviewed implementation commit: 83a433f9

## Verification Evidence

- Focused P1 retry-key tests: passed, 7/7.
- Full Line.Messaging.Tests: passed, 30/30.
- Solution build: passed with 0 errors; existing xUnit1012 warning remains in ChurchReport.MemberInfo.Tests.
- Generated bin/obj/artifacts folders: cleaned after verification; remaining count was 0.

## Gemini Review Result

`	ext
<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.6.WorktreeRefactorLine; dirty 1 paths.
Current task: none.
Active tasks: 3 total. Use `python ./.trellis/scripts/task.py list --mine` only if needed.
Spec indexes: 3 available.
&lt;/current-state&gt;

&lt;trellis-workflow&gt;
# Development Workflow - Session Summary
Full guide: .trellis/workflow.md. Step detail: `python ./.trellis/scripts/get_context.py --mode phase --step &lt;X.Y&gt;`.

## Phase Index

```
Phase 1: Plan    ??classify, get task-creation consent, then write planning artifacts
Phase 2: Execute ??implement only after task status is in_progress
Phase 3: Finish  ??verify, update spec, commit, and wrap up
```

### Request Triage

- Simple conversation or small task: ask only whether this turn should create a Trellis task. If the user says no, skip Trellis for this session.
- Complex task: ask whether you may create a Trellis task and enter planning. If the user says no, do not do broad inline implementation; explain, clarify scope, or suggest a smaller split.
- User approval to create a task is not approval to start implementation. Planning still happens first.

### Planning Artifacts

- `prd.md` ??requirements, constraints, and acceptance criteria. Do not put technical design or execution checklists here.
- `design.md` ??technical design for complex tasks: boundaries, contracts, data flow, tradeoffs, compatibility, rollout / rollback shape.
- `implement.md` ??execution plan for complex tasks: ordered checklist, validation commands, review gates, and rollback points.
- `implement.jsonl` / `check.jsonl` ??spec and research manifests for sub-agent context. They do not replace `implement.md`.
- Lightweight tasks may be PRD-only. Complex tasks must have `prd.md`, `design.md`, and `implement.md` before `task.py start`.

### Parent / Child Task Trees

Use a parent task when one user request contains several independently verifiable deliverables. The parent task owns the source requirement set, the task map, cross-child acceptance criteria, and final integration review; it normally should not be the implementation target unless it also has direct work.

Use child tasks for deliverables that can be planned, implemented, checked, and archived independently. Parent/child structure is not a dependency system: if one child must wait for another, write that ordering in the child `prd.md` / `implement.md` and keep each child's acceptance criteria testable.

Create new children with `task.py create "&lt;title&gt;" --slug &lt;name&gt; --parent &lt;parent-dir&gt;`. Link existing tasks with `task.py add-subtask &lt;parent&gt; &lt;child&gt;`, and unlink mistakes with `task.py remove-subtask &lt;parent&gt; &lt;child&gt;`.

### Phase 1: Plan
- 1.0 Create task `[required 繚 once]` (only after task-creation consent)
- 1.1 Requirement exploration `[required 繚 repeatable]` (`prd.md`; complex tasks also need `design.md` + `implement.md`)
- 1.2 Research `[optional 繚 repeatable]`
- 1.3 Configure context `[required 繚 once]` ??Claude Code, Cursor, OpenCode, Codex, Kiro, Gemini, Qoder, CodeBuddy, Copilot, Droid, Pi (sub-agent-dispatch platforms only; inline platforms skip)
- 1.4 Activate task `[required 繚 once]` (review gate, then `task.py start`; status ??in_progress)
- 1.5 Completion criteria

### Phase 2: Execute
- 2.1 Implement `[required 繚 repeatable]`
- 2.2 Quality check `[required 繚 repeatable]`
- 2.3 Rollback `[on demand]`

Sub-agent dispatch protocol applies to all platforms and all sub-agents, including class-2 Codex/Copilot/Gemini/Qoder and `trellis-research`: every dispatch prompt starts with `Active task: &lt;task path from task.py current&gt;` before role-specific instructions.

### Phase 3: Finish
- 3.2 Debug retrospective `[on demand]`
- 3.3 Spec update `[required 繚 once]`
- 3.4 Commit changes `[required 繚 once]`
- 3.5 Wrap-up reminder

&gt; Note: step 3.1 was folded into 2.2 (last-iteration full-scope check) and 3.4 (commit preamble). Numbering kept stable to avoid breaking external references.

### Rules

1. Identify which Phase you're in, then continue from the next step there
2. Run steps in order inside each Phase; `[required]` steps can't be skipped
3. Phases can roll back (e.g., Execute reveals a prd defect ??return to Plan to fix, then re-enter Execute)
4. Steps tagged `[once]` are skipped if the output already exists; don't re-run
5. Artifact presence informs the next step; missing `design.md` / `implement.md` is valid for lightweight tasks and incomplete planning for complex tasks.

### Active Task Routing

When a user request matches one of these intents inside an active task, route first, then load the detailed phase step if needed.

- Planning or unclear requirements -&gt; `trellis-brainstorm`.
- `in_progress` implementation/check -&gt; dispatch `trellis-implement` / `trellis-check`.
- Repeated debugging -&gt; `trellis-break-loop`; spec updates -&gt; `trellis-update-spec`.

- Planning or unclear requirements -&gt; `trellis-brainstorm`.
- Before editing -&gt; `trellis-before-dev`; after editing -&gt; `trellis-check`.
- Repeated debugging -&gt; `trellis-break-loop`; spec updates -&gt; `trellis-update-spec`.

### Guardrails

- Task creation approval is not implementation approval; implementation waits for `task.py start` after artifact review.
- PRD-only is valid for lightweight tasks; complex tasks need `design.md` + `implement.md`.
- Planning must be persisted to task artifacts; checks must run before reporting completion.

### Loading Step Detail

At each step, run this to fetch detailed guidance:

```bash
python ./.trellis/scripts/get_context.py --mode phase --step &lt;step&gt;
# e.g. python ./.trellis/scripts/get_context.py --mode phase --step 1.1
```

---
&lt;/trellis-workflow&gt;

&lt;guidelines&gt;
Task context order for implementation/check: jsonl entries -&gt; `prd.md` -&gt; `design.md if present` -&gt; `implement.md if present`. Missing optional artifacts are skipped for lightweight tasks.

## Available indexes (read on demand)
- .trellis/spec/guides/index.md
- .trellis/spec/backend/index.md
- .trellis/spec/frontend/index.md

Discover more via: `python ./.trellis/scripts/get_context.py --mode packages`
&lt;/guidelines&gt;

&lt;task-status&gt;
Status: NO ACTIVE TASK
Next-Action: Classify the current turn before creating any Trellis task. Simple conversation / small task asks only whether this turn should create a Trellis task. Complex task asks whether task creation and planning are allowed.
&lt;/task-status&gt;

&lt;ready&gt;
Context loaded. Follow &lt;task-status&gt;. Load workflow/spec/task details only when needed.
&lt;/ready&gt;</hook_context>

# Gemini Role: UI Reviewer

> For: /ccg:review, /ccg:bugfix validation, /ccg:dev Phase 5

You are a senior UI reviewer specializing in frontend code quality, accessibility, and design system compliance.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Structured review with scores (for bugfix validation)
- **Focus**: UX, accessibility, consistency, performance

## Review Checklist

### Accessibility (Critical)
- [ ] Semantic HTML structure
- [ ] ARIA labels and roles present
- [ ] Keyboard navigable
- [ ] Focus visible and managed
- [ ] Color contrast sufficient

### Design Consistency
- [ ] Uses design system tokens
- [ ] No hardcoded colors/sizes
- [ ] Consistent spacing and typography
- [ ] Follows existing component patterns

### Code Quality
- [ ] TypeScript types complete
- [ ] Props interface clear
- [ ] No inline styles (unless justified)
- [ ] Component is reusable
- [ ] Proper event handling

### Performance
- [ ] No unnecessary re-renders
- [ ] Proper memoization where needed
- [ ] Lazy loading for heavy components
- [ ] Image optimization

### Responsive
- [ ] Works on mobile
- [ ] Works on tablet
- [ ] Works on desktop
- [ ] No horizontal scroll issues

## Scoring Format (for /ccg:bugfix)

```
VALIDATION REPORT
=================
User Experience: XX/20 - [reason]
Visual Consistency: XX/20 - [reason]
Accessibility: XX/20 - [reason]
Performance: XX/20 - [reason]
Browser Compatibility: XX/20 - [reason]

TOTAL SCORE: XX/100

ISSUES FOUND:
- [issue 1]
- [issue 2]

RECOMMENDATION: [PASS/NEEDS_IMPROVEMENT]
```

## Response Structure

1. **Summary** - Overall assessment
2. **Accessibility Issues** - a11y problems found
3. **Design Issues** - Inconsistencies
4. **Suggestions** - Improvements
5. **Positive Notes** - What's done well

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/coding-style.md` as the primary review standard
2. Read `.context/prefs/workflow.md` to verify the full development flow was followed (tests written, docs updated, etc.)
3. Check `.context/history/commits.jsonl` for past decisions on the same components ??flag if current changes contradict previous design decisions without justification

<TASK>
Review the committed LINE SDK P1 retry-key implementation in this repository.

Scope:
- Commit range: HEAD~1..HEAD
- Feature: Add LINE Messaging API X-Line-Retry-Key support to typed PushMessageAsync, MultiCastMessageAsync, and BroadcastMessageAsync.
- Preserve old overload compatibility.
- Keep LINE protocol details inside Line.Messaging.
- No ChurchReport, CRM, payment, donation, webhook, LinePayCSharp, Audience, Narrowcast, or P2 changes.

Review criteria:
1. Correctness against LINE Messaging API retry-key semantics.
2. Public API compatibility and nullable behavior.
3. SDK/product boundary cleanliness.
4. Test adequacy, including header presence/absence, endpoint stability, and body stability.
5. Scope creep, hidden global state, special cases, or maintainability issues.
6. Any build/test risk from the implementation.

Output Critical / Warning / Info findings. Include file paths and line references where possible. If there are no findings in a severity, say None.

<DIFF>
diff --git a/.ccg/tasks/line-messaging-sdk-p1-brainstorm/task.json b/.ccg/tasks/line-messaging-sdk-p1-brainstorm/task.json
index 6780d693..f8fe686c 100644
--- a/.ccg/tasks/line-messaging-sdk-p1-brainstorm/task.json
+++ b/.ccg/tasks/line-messaging-sdk-p1-brainstorm/task.json
@@ -5,8 +5,8 @@
     "complexity":  "M",
     "risk":  "medium",
     "domain":  "backend",
-    "currentPhase":  "planning",
-    "nextAction":  "Implementation plan written; await inline execution approval",
+    "currentPhase":  "implementation",
+    "nextAction":  "Run external Gemini and Claude review for LINE SDK P1 retry-key changes",
     "createdAt":  "2026-07-02T16:00:00+08:00",
     "branch":  "Jesus_5.1.6.WorktreeRefactorLine"
 }
diff --git a/Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs b/Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs
new file mode 100644
index 00000000..a120a3b4
--- /dev/null
+++ b/Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs
@@ -0,0 +1,139 @@
+using FluentAssertions;
+using Line.Messaging;
+using Newtonsoft.Json.Linq;
+using System.Net;
+using System.Text;
+using Xunit;
+
+namespace Line.Messaging.Tests;
+
+public sealed class LineMessagingClientP1RetryKeyTests
+{
+    [Fact]
+    public async Task Push_message_with_retry_key_sends_line_retry_header()
+    {
+        var handler = new CapturingHttpMessageHandler();
+        var client = CreateClient(handler);
+
+        await client.PushMessageAsync(
+            "U1234567890abcdef",
+            new List<ISendMessage> { new TextMessage("payment received") },
+            "fee-1001-notification");
+
+        handler.Requests.Should().ContainSingle();
+        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
+        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
+        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
+        values.Should().ContainSingle().Which.Should().Be("fee-1001-notification");
+
+        var body = JObject.Parse(handler.Bodies[0]);
+        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
+        body["messages"]!.Should().HaveCount(1);
+    }
+
+    [Fact]
+    public async Task Push_message_existing_overload_does_not_send_retry_header()
+    {
+        var handler = new CapturingHttpMessageHandler();
+        var client = CreateClient(handler);
+
+        await client.PushMessageAsync(
+            "U1234567890abcdef",
+            new List<ISendMessage> { new TextMessage("payment received") });
+
+        handler.Requests.Should().ContainSingle();
+        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
+        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
+    }
+
+    [Fact]
+    public async Task Multicast_message_with_retry_key_sends_line_retry_header_and_keeps_body()
+    {
+        var handler = new CapturingHttpMessageHandler();
+        var client = CreateClient(handler);
+
+        await client.MultiCastMessageAsync(
+            new List<string> { "U111", "U222" },
+            new List<ISendMessage> { new TextMessage("batch notice") },
+            "batch-20260702-001");
+
+        handler.Requests.Should().ContainSingle();
+        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
+        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/multicast");
+        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
+        values.Should().ContainSingle().Which.Should().Be("batch-20260702-001");
+
+        var body = JObject.Parse(handler.Bodies[0]);
+        body["to"]!.Select(token => token.Value<string>()).Should().Equal("U111", "U222");
+        body["messages"]!.Should().HaveCount(1);
+    }
+
+    [Fact]
+    public async Task Broadcast_message_with_retry_key_sends_line_retry_header()
+    {
+        var handler = new CapturingHttpMessageHandler();
+        var client = CreateClient(handler);
+
+        await client.BroadcastMessageAsync(
+            new List<ISendMessage> { new TextMessage("global notice") },
+            "broadcast-20260702-001");
+
+        handler.Requests.Should().ContainSingle();
+        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
+        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/broadcast");
+        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
+        values.Should().ContainSingle().Which.Should().Be("broadcast-20260702-001");
+    }
+
+    [Theory]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData("   ")]
+    public async Task Empty_retry_key_does_not_send_retry_header(string? retryKey)
+    {
+        var handler = new CapturingHttpMessageHandler();
+        var client = CreateClient(handler);
+
+        await client.PushMessageAsync(
+            "U1234567890abcdef",
+            new List<ISendMessage> { new TextMessage("payment received") },
+            retryKey);
+
+        handler.Requests.Should().ContainSingle();
+        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
+    }
+
+    private static LineMessagingClient CreateClient(CapturingHttpMessageHandler handler)
+    {
+        var httpClient = new HttpClient(handler);
+        return new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
+    }
+
+    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
+    {
+        private readonly HttpStatusCode _statusCode;
+        private readonly string _responseBody;
+        private readonly string _mediaType;
+
+        public CapturingHttpMessageHandler(string responseBody = "{}", HttpStatusCode statusCode = HttpStatusCode.OK, string mediaType = "application/json")
+        {
+            _responseBody = responseBody;
+            _statusCode = statusCode;
+            _mediaType = mediaType;
+        }
+
+        public List<HttpRequestMessage> Requests { get; } = new();
+
+        public List<string> Bodies { get; } = new();
+
+        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
+        {
+            Requests.Add(request);
+            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
+            return new HttpResponseMessage(_statusCode)
+            {
+                Content = new StringContent(_responseBody, Encoding.UTF8, _mediaType)
+            };
+        }
+    }
+}
diff --git a/Line.Messaging/ILineMessagingClient.cs b/Line.Messaging/ILineMessagingClient.cs
index f62eb0ed..9f457048 100644
--- a/Line.Messaging/ILineMessagingClient.cs
+++ b/Line.Messaging/ILineMessagingClient.cs
@@ -45,6 +45,14 @@ namespace Line.Messaging
         /// <param name="messages">Reply messages. Up to 5 messages.</param>
         Task PushMessageAsync(string to, IList<ISendMessage> messages);
 
+        /// <summary>
+        /// Send messages to a user, group, or room with LINE retry-key support.
+        /// </summary>
+        /// <param name="to">ID of the receiver</param>
+        /// <param name="messages">Reply messages. Up to 5 messages.</param>
+        /// <param name="retryKey">Optional LINE retry key for idempotent retries. Null or whitespace means no retry header.</param>
+        Task PushMessageAsync(string to, IList<ISendMessage> messages, string? retryKey);
+
         /// <summary>
         /// Send messages to a user, group, or room at any time.
         /// Note: Use of push messages are limited to certain plans.
@@ -70,6 +78,14 @@ namespace Line.Messaging
         /// <param name="messages">Reply messages. Up to 5 messages.</param>
         Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages);
 
+        /// <summary>
+        /// Send push messages to multiple users with LINE retry-key support.
+        /// </summary>
+        /// <param name="to">IDs of the receivers. Max: 500 users</param>
+        /// <param name="messages">Reply messages. Up to 5 messages.</param>
+        /// <param name="retryKey">Optional LINE retry key for idempotent retries. Null or whitespace means no retry header.</param>
+        Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages, string? retryKey);
+
         /// <summary>
         /// Send push messages to multiple users at any time.
         /// Only available for plans which support push messages. Messages cannot be sent to groups or rooms
@@ -96,6 +112,13 @@ namespace Line.Messaging
         /// <param name="messages">Messages to send. Max: 5 messages</param>
         Task BroadcastMessageAsync(IList<ISendMessage> messages);
 
+        /// <summary>
+        /// Broadcasts messages with LINE retry-key support.
+        /// </summary>
+        /// <param name="messages">Messages to send. Max: 5 messages</param>
+        /// <param name="retryKey">Optional LINE retry key for idempotent retries. Null or whitespace means no retry header.</param>
+        Task BroadcastMessageAsync(IList<ISendMessage> messages, string? retryKey);
+
         /// <summary>
         /// Sends push messages to multiple users specified by attributes (such as gender, age, OS, region, friendship duration) or retargeting (audiences).
         /// https://developers.line.biz/en/reference/messaging-api/#send-narrowcast-message
diff --git a/Line.Messaging/LineMessagingClient.cs b/Line.Messaging/LineMessagingClient.cs
index 07256b45..e97859ba 100644
--- a/Line.Messaging/LineMessagingClient.cs
+++ b/Line.Messaging/LineMessagingClient.cs
@@ -165,6 +165,21 @@ namespace Line.Messaging
             return CombineBaseAndPath(_dataUri, path);
         }
 
+        private static void ApplyRetryKeyHeader(HttpRequestMessage request, string? retryKey)
+        {
+            if (request == null)
+            {
+                throw new ArgumentNullException(nameof(request));
+            }
+
+            if (string.IsNullOrWhiteSpace(retryKey))
+            {
+                return;
+            }
+
+            request.Headers.TryAddWithoutValidation("X-Line-Retry-Key", retryKey);
+        }
+
         private static string CombineBaseAndPath(string baseUri, string path)
         {
             if (string.IsNullOrWhiteSpace(baseUri))
@@ -236,7 +251,7 @@ namespace Line.Messaging
         /// </example>
         public static async Task<ChannelAccessToken> IssueChannelAccessTokenAsync(HttpClient httpClient, string channelId, string channelAccessToken, string uri = DEFAULT_URI)
         {
-            var response = await httpClient.PostAsync($"{uri}/oauth/accessToken",
+            var response = await httpClient.PostAsync(CombineBaseAndPath(NormalizeLineApiBaseUri(uri), "/oauth/accessToken"),
                 new FormUrlEncodedContent(new Dictionary<string, string>
                 {
                     ["grant_type"] = "client_credentials",
@@ -292,7 +307,7 @@ namespace Line.Messaging
         /// </example>
         public static async Task RevokeChannelAccessTokenAsync(HttpClient httpClient, string channelAccessToken, string uri = DEFAULT_URI)
         {
-            var response = await httpClient.PostAsync($"{uri}/oauth/revoke",
+            var response = await httpClient.PostAsync(CombineBaseAndPath(NormalizeLineApiBaseUri(uri), "/oauth/revoke"),
                 new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = channelAccessToken })).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
@@ -542,9 +557,13 @@ namespace Line.Messaging
         /// </example>
         /// <seealso cref="ReplyMessageAsync(string, IList{ISendMessage})"/>
         /// <seealso cref="MultiCastMessageAsync(IList{string}, IList{ISendMessage})"/>
-        public virtual async Task PushMessageAsync(string to, IList<ISendMessage> messages)
+        public virtual Task PushMessageAsync(string to, IList<ISendMessage> messages)
+            => PushMessageAsync(to, messages, retryKey: null);
+
+        public virtual async Task PushMessageAsync(string to, IList<ISendMessage> messages, string? retryKey)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/push");
+            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/push"));
+            ApplyRetryKeyHeader(request, retryKey);
             request.Content = new StringContent(JsonConvert.SerializeObject(new { to, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
             var response = await _client.SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
@@ -636,9 +655,13 @@ namespace Line.Messaging
         /// });
         /// </code>
         /// </example>
-        public virtual async Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages)
+        public virtual Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages)
+            => MultiCastMessageAsync(to, messages, retryKey: null);
+
+        public virtual async Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages, string? retryKey)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/multicast");
+            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/multicast"));
+            ApplyRetryKeyHeader(request, retryKey);
             request.Content = new StringContent(JsonConvert.SerializeObject(new { to, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
             var response = await _client.SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
@@ -710,9 +733,13 @@ namespace Line.Messaging
         /// });
         /// </code>
         /// </example>
-        public virtual async Task BroadcastMessageAsync(IList<ISendMessage> messages)
+        public virtual Task BroadcastMessageAsync(IList<ISendMessage> messages)
+            => BroadcastMessageAsync(messages, retryKey: null);
+
+        public virtual async Task BroadcastMessageAsync(IList<ISendMessage> messages, string? retryKey)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/broadcast");
+            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/broadcast"));
+            ApplyRetryKeyHeader(request, retryKey);
             request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
             var response = await _client.SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);

</DIFF>
</TASK>
OUTPUT: Critical/Warning/Info review report with concise rationale and concrete fix suggestions.
Trellis SessionStart 銝??歇頛??
# LINE SDK P1 Retry-Key Implementation Review Report

This review covers the implementation of the `X-Line-Retry-Key` header support added to `PushMessageAsync`, `MultiCastMessageAsync`, and `BroadcastMessageAsync` within the `Line.Messaging` library.

---

## 1. Summary of Assessment
The implementation is exceptionally clean, robust, and correctly conforms to both the LINE Messaging API specs and the project's technical guidelines. The new overloads correctly handle the `X-Line-Retry-Key` header dynamically per-request, and backward compatibility is fully preserved for the existing public API overloads.

---

## 2. Findings by Severity

### Critical
**None**

### Warning
* **Public Interface Extension (Potential Breaking Change for Custom Implementations)**
  * **File:** `Line.Messaging/ILineMessagingClient.cs` (Lines 45-53, 70-86, 96-119)
  * **Description:** Adding new methods to the public interface `ILineMessagingClient` is technically a source/binary breaking change for any external client libraries or consumers that implement this interface customly.
  * **Rationale:** While perfectly safe within the context of the workspace (where only `LineMessagingClient` implements the interface), if this SDK is distributed as a NuGet package, it would require a minor/major version bump depending on the semver policy.
  * **Recommendation:** Keep as is if internal-only; if distributed publicly, default interface implementations (DIMs) supported since C# 8.0 could be leveraged (e.g., declaring `Task PushMessageAsync(...) => Task.CompletedTask;` directly on the interface), though standard overload delegation is preferred for clean design.

### Info
* **Refactoring in OAuth Helper Methods (Out of Scope but Safe)**
  * **File:** `Line.Messaging/LineMessagingClient.cs` (Lines 254, 310)
  * **Description:** The static methods `IssueChannelAccessTokenAsync` and `RevokeChannelAccessTokenAsync` were refactored to use `CombineBaseAndPath(NormalizeLineApiBaseUri(uri), ...)` instead of simple string interpolation `$"{uri}/..."`.
  * **Rationale:** While technically outside the scope of adding retry keys to push/multicast/broadcast, this cleanup fixes custom-base URI inconsistencies and aligns the static methods with the normalized URL patterns established elsewhere in the SDK. It does not introduce any risks.
* **Test Coverage Quality**
  * **File:** `Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs` (Lines 1-139)
  * **Description:** Excellent coverage. The tests successfully verify header presence, header values, header absence (for null/empty/whitespace/default overloads), API endpoint correctness, and that request bodies are preserved completely.

---

## 3. Scope Compliance & Design Cleanliness
* **Ecosystem Limits:** Zero dependency changes or leaks. No domain business logic (`ChurchReport`, `Payments`, CRM) leaked into the SDK.
* **Encapsulation:** Protocol details (`X-Line-Retry-Key` header injection) are entirely encapsulated within the internal helper `ApplyRetryKeyHeader` inside `LineMessagingClient.cs`.
* **State Management:** Stateless and thread-safe. Headers are added specifically to transient `HttpRequestMessage` instances rather than changing state on the `HttpClient` level.

---
SESSION_ID: 30965654-1aac-471a-8ba6-0e6f0e25f04b

`

## Claude Review Status

`	ext
Claude reviewer could not be executed at this time.

Observed command:
'Say OK only.' | claude -p --dangerously-skip-permissions --output-format text -

Observed result:
You've hit your session limit; Claude CLI exited with code 1.

Impact:
P1 implementation commit exists and Gemini review output exists, but the required Gemini + Claude dual-model review gate is not complete until Claude quota resets and the Claude reviewer can run.

`

User explicitly approved continuing without waiting for Claude quota recovery. Therefore Claude review is waived for this P1 gate.

## Lead Disposition

- Critical: None confirmed.
- Warning: Gemini flagged public interface expansion as a versioning concern for external custom implementers. Accepted for this internal SDK worktree; if packaged later, document as a versioned API addition.
- Info: OAuth URL normalization was kept because it preserves the existing P0 endpoint contract and full Line.Messaging.Tests passed after the fix.
- Action: No code changes required before moving out of P1 first-round implementation.

## P1 Completion Decision

P1 first-round scope is complete: retry-key support exists for typed push, multicast, and broadcast APIs, old overloads remain compatible, retry-key header logic is centralized in Line.Messaging, tests cover presence/absence/body/endpoint behavior, and generated outputs are cleaned.
