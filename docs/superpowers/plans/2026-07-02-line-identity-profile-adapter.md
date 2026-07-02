# LINE Identity Profile Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a small, reusable `LineMessagingProcessor` profile lookup adapter so ChurchReport and future ASP.NET Core products can fetch LINE user profiles without importing ChurchReport CRM, route, or LIFF behavior.

**Architecture:** Keep the boundary narrow. `Line.Messaging` owns official LINE HTTP protocol and `UserProfile` models; `LineMessagingProcessor` validates product-neutral inputs and delegates to the SDK; ChurchReport keeps CRM binding, `new_lineid`, controller decisions, and LIFF pages.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, `Line.Messaging.LineMessagingClient`, `LineMessagingProcessor.LineMessagingProcessorClass`.

---

## Scope

Included:

- Add SDK-backed `LineMessagingProcessorClass.GetUserProfileAsync(string UserId)`.
- Validate blank `UserId` before any HTTP call.
- Return the SDK `Line.Messaging.UserProfile` directly.
- Keep the existing legacy `GetUserProfile(string UserId)` method compatible by redirecting it to the new async adapter.
- Add focused processor tests.
- Verify processor tests, SDK tests, and full solution build.
- Clean generated `bin/`, `obj/`, and `artifacts` directories.

Excluded:

- Do not modify ChurchReport login controllers.
- Do not move CRM lookup, `new_lineid`, `LineIdLogin`, or LIFF page logic into `LineMessagingProcessor`.
- Do not implement broad P2 LINE official API coverage.
- Do not change `Line.Messaging.LineMessagingClient.GetUserProfileAsync(...)`; it already owns the official `/v2/bot/profile/{userId}` API call.

## File Structure

- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`
  - Add the product-neutral async adapter.
  - Preserve the old method signature by delegating to the new adapter.
  - Keep detailed Traditional Chinese comments near the new adapter to explain the boundary.
- Create: `LineMessagingProcessor.Tests/LineMessagingProcessorIdentityProfileTests.cs`
  - Tests the adapter from the processor layer.
  - Uses an in-memory `HttpMessageHandler`; no real LINE network calls.
- Modify: `.ccg/tasks/line-identity-profile-adapter/task.json`
  - Move from planning to implementation/review/completed as execution progresses.
- Modify or create: `.ccg/tasks/line-identity-profile-adapter/review.md`
  - Record Gemini/Claude review results. If Claude wrapper still fails, document the exact command and failure instead of blocking forever.

## Boundary Rules

- `Line.Messaging` may know endpoint paths such as `/bot/profile/{userId}`.
- `LineMessagingProcessor` may know that `UserId` must not be blank.
- `LineMessagingProcessor` must not know ChurchReport CRM fields, contacts, donors, members, MVC routes, or LIFF page names.
- ChurchReport remains responsible for deciding how a returned `UserProfile` maps to product identity.

---

### Task 1: Add Failing Identity Profile Tests

**Files:**
- Create: `LineMessagingProcessor.Tests/LineMessagingProcessorIdentityProfileTests.cs`

- [ ] **Step 1: Create the test file**

Add this file exactly:

```csharp
using System.Net;
using FluentAssertions;
using Line.Messaging;
using Xunit;

namespace LineMessagingProcessor.Tests;

public sealed class LineMessagingProcessorIdentityProfileTests
{
    [Fact]
    public async Task GetUserProfileAsync_delegates_to_line_sdk_profile_endpoint()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Test User","userId":"U1234567890abcdef","pictureUrl":"https://example.com/u.png","statusMessage":"hello"}""");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        var profile = await processor.GetUserProfileAsync("U1234567890abcdef");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/profile/U1234567890abcdef");
        profile.DisplayName.Should().Be("Test User");
        profile.UserId.Should().Be("U1234567890abcdef");
        profile.PictureUrl.Should().Be("https://example.com/u.png");
        profile.StatusMessage.Should().Be("hello");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetUserProfileAsync_rejects_blank_user_id_before_http_call(string? userId)
    {
        var handler = new CapturingHttpMessageHandler("{}");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        Func<Task> action = () => processor.GetUserProfileAsync(userId!);

        var exception = await action.Should().ThrowAsync<ArgumentException>();
        exception.Which.ParamName.Should().Be("UserId");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserProfile_keeps_legacy_signature_by_using_sdk_backed_adapter()
    {
        var handler = new CapturingHttpMessageHandler(
            """{"displayName":"Legacy User","userId":"Ulegacy","pictureUrl":"https://example.com/legacy.png","statusMessage":"legacy"}""");
        var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
        var processor = new LineMessagingProcessorClass(sdkClient);

        var profile = await processor.GetUserProfile("Ulegacy");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/profile/Ulegacy");
        profile.DisplayName.Should().Be("Legacy User");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            });
        }
    }
}
```

- [ ] **Step 2: Run the focused tests and confirm RED**

Run:

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorIdentityProfileTests -v minimal
```

Expected:

- Build fails because `LineMessagingProcessorClass` does not yet contain `GetUserProfileAsync`.
- No real LINE network request is made.

- [ ] **Step 3: Commit is not allowed yet**

Do not commit a RED-only test. Continue to Task 2.

---

### Task 2: Implement the SDK-Backed Profile Adapter

**Files:**
- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`

- [ ] **Step 1: Replace the old RestSharp profile method with a thin adapter**

Find the existing method:

```csharp
public async Task<UserProfile> GetUserProfile(string UserId)
{
    var request = new RestRequest($"profile/{UserId}");
    request.AddHeader("Content-Type", "application/json; charset=UTF-8");
    request.AddHeader("Authorization", GetRequiredChannelAccessToken());

    var response = await _restClient.GetAsync(request);

    if (response != null && response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
    {
        return JsonConvert.DeserializeObject<UserProfile>(response.Content);
    }

    return null;
}
```

Replace it with:

```csharp
/// <summary>
/// 以 SDK 取得 LINE 使用者個人資料。
/// 這一層只做「可重用的 LINE 身分查詢」：檢查 UserId 是否有值，然後交給
/// Line.Messaging SDK 呼叫官方 /bot/profile/{userId} API。
/// ChurchReport 的 CRM 聯絡人查詢、new_lineid 綁定、登入流程與 LIFF 頁面都不放在這裡，
/// 避免未來其他 ASP.NET Core 產品重用 LINE 模組時被 ChurchReport 的產品流程綁住。
/// </summary>
/// <param name="UserId">LINE 使用者 ID。不可為 null、空字串或只包含空白。</param>
/// <returns>LINE 官方回傳的使用者個人資料。</returns>
/// <exception cref="ArgumentException">UserId 空白時拋出，且不發出 HTTP request。</exception>
public async Task<UserProfile> GetUserProfileAsync(string UserId)
{
    if (string.IsNullOrWhiteSpace(UserId))
    {
        throw new ArgumentException("UserId is required.", nameof(UserId));
    }

    return await _lineMessagingClient.GetUserProfileAsync(UserId).ConfigureAwait(false);
}

/// <summary>
/// 舊版同步命名的相容入口。
/// 保留這個方法是為了不一次破壞既有 ChurchReport 呼叫端；實際資料流已改走
/// GetUserProfileAsync，讓新舊入口共用同一份 SDK-backed 實作。
/// </summary>
/// <param name="UserId">LINE 使用者 ID。</param>
/// <returns>LINE 官方回傳的使用者個人資料。</returns>
public Task<UserProfile> GetUserProfile(string UserId)
{
    return GetUserProfileAsync(UserId);
}
```

Important:

- Do not change `GetUserDisplayName` yet; it should keep compiling because it awaits `GetUserProfile(UserId)`.
- Do not remove `_restClient` in this task because `SendMessage(...)` still uses it.
- Do not add ChurchReport-specific mapping.

- [ ] **Step 2: Run the focused tests and confirm GREEN**

Run:

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorIdentityProfileTests -v minimal
```

Expected:

- All `LineMessagingProcessorIdentityProfileTests` pass.

- [ ] **Step 3: Run existing reliable processor tests**

Run:

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorReliableNotificationTests -v minimal
```

Expected:

- Existing reliable notification tests still pass.

- [ ] **Step 4: Commit the adapter slice**

Run:

```powershell
git add LineMessagingProcessor\LineMessagingProcessorClass.cs LineMessagingProcessor.Tests\LineMessagingProcessorIdentityProfileTests.cs
git commit -m "feat: add LINE identity profile adapter"
```

---

### Task 3: Full Verification

**Files:**
- No source changes expected unless verification finds a real defect.

- [ ] **Step 1: Run processor tests**

Run:

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal
```

Expected:

- All processor tests pass.

- [ ] **Step 2: Run SDK tests**

Run:

```powershell
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal
```

Expected:

- All SDK tests pass.

- [ ] **Step 3: Run full solution build**

Run:

```powershell
dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false
```

Expected:

- Build succeeds with 0 errors.

- [ ] **Step 4: Check boundaries**

Run:

```powershell
rg -n "new_lineid|LineIdLogin|RetrieveContactEntityByLineUserId|Controller|IActionResult|Microsoft\.Xrm|CRM|Contact" LineMessagingProcessor --glob "*.cs"
```

Expected:

- No new ChurchReport-specific identity or CRM behavior appears in `LineMessagingProcessor`.
- Existing unrelated strings should be manually reviewed before changing anything.

- [ ] **Step 5: Clean generated output**

Run:

```powershell
Get-ChildItem -Path . -Directory -Recurse -Force |
  Where-Object { $_.Name -in @('bin','obj','artifacts') } |
  ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
```

Then verify:

```powershell
Get-ChildItem -Path . -Directory -Recurse -Force |
  Where-Object { $_.Name -in @('bin','obj','artifacts') } |
  Select-Object -ExpandProperty FullName
```

Expected:

- No output.

- [ ] **Step 6: Verify UTF-8 without BOM and CRLF for touched text files**

Run:

```powershell
$files = @(
  'LineMessagingProcessor\LineMessagingProcessorClass.cs',
  'LineMessagingProcessor.Tests\LineMessagingProcessorIdentityProfileTests.cs',
  '.ccg\tasks\line-identity-profile-adapter\task.json',
  'docs\superpowers\plans\2026-07-02-line-identity-profile-adapter.md'
)
foreach ($file in $files) {
  $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $file))
  $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
  $text = [System.Text.Encoding]::UTF8.GetString($bytes)
  $hasLfOnly = [regex]::IsMatch($text, '(?<!`r)`n')
  [pscustomobject]@{ File = $file; HasBom = $hasBom; HasLfOnly = $hasLfOnly }
}
```

Expected:

- `HasBom = False`
- `HasLfOnly = False`

---

### Task 4: External Review and Task Closure

**Files:**
- Create or modify: `.ccg/tasks/line-identity-profile-adapter/review.md`
- Modify: `.ccg/tasks/line-identity-profile-adapter/task.json`

- [ ] **Step 1: Run Gemini reviewer**

Run:

```powershell
@'
ROLE_FILE: ~/.claude/.ccg/prompts/gemini/reviewer.md
<TASK>
Review the current git diff for the LINE identity profile adapter.
Focus on:
- Whether LineMessagingProcessor remains product-neutral.
- Whether blank UserId validation happens before HTTP.
- Whether legacy GetUserProfile compatibility is preserved.
- Whether tests cover endpoint, returned fields, and no-HTTP validation.
</TASK>
OUTPUT: Critical/Warning/Info review report
'@ | ~/.claude/bin/codeagent-wrapper --progress --backend gemini - "$(pwd)"
```

Expected:

- Review report is produced.
- Critical findings must be fixed before closure.

- [ ] **Step 2: Try Claude reviewer and document result**

Run:

```powershell
@'
ROLE_FILE: ~/.claude/.ccg/prompts/claude/reviewer.md
<TASK>
Review the current git diff for the LINE identity profile adapter.
Focus on:
- Whether LineMessagingProcessor remains product-neutral.
- Whether blank UserId validation happens before HTTP.
- Whether legacy GetUserProfile compatibility is preserved.
- Whether tests cover endpoint, returned fields, and no-HTTP validation.
</TASK>
OUTPUT: Critical/Warning/Info review report
'@ | ~/.claude/bin/codeagent-wrapper --progress --backend claude - "$(pwd)"
```

Expected:

- If Claude succeeds, include its report in `review.md`.
- If the wrapper fails again, record the command, exit code, and stderr summary in `review.md`; do not block indefinitely.

- [ ] **Step 3: Write review.md**

Use this structure:

```markdown
# LINE Identity Profile Adapter Review

Date: 2026-07-02
Branch: Jesus_5.1.6.WorktreeRefactorLine

## Verification

- LineMessagingProcessor.Tests: PASS/FAIL
- Line.Messaging.Tests: PASS/FAIL
- Solution build: PASS/FAIL
- Boundary search: PASS/FAIL
- UTF-8 without BOM + CRLF check: PASS/FAIL

## Gemini Review

Paste the Gemini Critical/Warning/Info report here.

## Claude Review

Paste the Claude report here, or document the wrapper failure with exact evidence.

## Resolution

- Critical: none, or list each fixed item.
- Warning: none, accepted, or fixed.
- Info: noted.
```

- [ ] **Step 4: Mark task completed**

Update `.ccg/tasks/line-identity-profile-adapter/task.json`:

```json
{
  "id": "line-identity-profile-adapter",
  "title": "Add reusable LINE identity profile adapter",
  "status": "completed",
  "complexity": "M",
  "risk": "medium",
  "domain": "backend",
  "currentPhase": "completed",
  "nextAction": "Ready for next P1 or selected P2 LINE slice",
  "createdAt": "2026-07-02T18:10:00+08:00",
  "branch": "Jesus_5.1.6.WorktreeRefactorLine"
}
```

- [ ] **Step 5: Commit task metadata and review**

Run:

```powershell
git add .ccg\tasks\line-identity-profile-adapter docs\superpowers\plans\2026-07-02-line-identity-profile-adapter.md
git commit -m "chore: plan LINE identity profile adapter"
```

If implementation and review were already committed in earlier steps, keep this metadata commit separate.

---

## Self-Review

- Spec coverage: The plan implements the approved P1 slice only: SDK-backed user profile lookup, blank ID validation, legacy compatibility, tests, verification, and review.
- Placeholder scan: No `TBD`, `TODO`, or broad unspecified implementation steps remain.
- Type consistency: The plan uses existing `Line.Messaging.UserProfile`, `LineMessagingClient.GetUserProfileAsync(string userId)`, and `LineMessagingProcessorClass`.
- Boundary check: No ChurchReport CRM, route, controller, or LIFF behavior is moved into reusable modules.
- Linus-style maintenance check: One concern per layer, direct data flow, no hidden global state, no speculative P2 expansion.
