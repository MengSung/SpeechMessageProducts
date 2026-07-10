# Full Code Quality Audit And Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver one comprehensive repair set for confirmed Session Leak, cross-user leakage, memory growth, socket exhaustion, slow request, and sync-over-async risks in the current solution.

**Architecture:** Fix high-confidence static defects first, using existing DI and workflow boundaries instead of new infrastructure. LINE and HTTP calls must use DI-created clients or explicit ownership; session/cache objects must be stateless, per-session keyed, bounded, and cleaned on eviction; request-path I/O must use async paths where the calling surface can support it. Runtime-only claims remain separate from static fixes until measured with counters or load tests.

**Tech Stack:** .NET 10, ASP.NET Core MVC with legacy routing, xUnit, FluentAssertions, Microsoft.Extensions.Caching.Memory, IHttpClientFactory, Line.Messaging, LineMessagingProcessor.Workflows, Dynamics CRM SDK.

---

## Scope Check

The approved design spans several subsystems, but the user requested a single comprehensive repair delivery. This plan keeps one delivery while splitting implementation into independent, verifiable workstreams. If one runtime-only verification cannot be run locally, the final report must mark that item as "fixed statically, runtime proof pending" rather than claiming measured improvement.

Do not modify product code before execution is approved. This plan file and the CCG task metadata are the only files changed during planning.

## File Map

Create:

- `ChurchReport.MemberInfo.Tests/Security/SessionValidationMiddlewareTests.cs`: verifies session invalidation commits asynchronously and redirects without invoking the next middleware.
- `ChurchReport.MemberInfo.Tests/Security/CheckSessionOutAttributeTests.cs`: verifies the legacy session attribute has no per-request instance state and no async void override.
- `ChurchReport.MemberInfo.Tests/Caching/InMemoryDataContextSmallGroupCacheTests.cs`: verifies session cache entries use bounded expiration and dispose cached disposable values on eviction.
- `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineClientOwnershipScanTests.cs`: protects ChurchReport production code from new `new LineMessagingClient(token)` call sites.
- `ChurchReport.MemberInfo.Tests/Security/PersonalImageAuthorizationTests.cs`: regression tests for `/Personal/GetContactImage` and `/Personal/GetContactImagesBatch` object access.
- `.ccg/dual-model-runs/full-code-quality-audit-and-fix-review-input.md`: final review prompt for the CCG self-healing runner.

Modify:

- `SpeechMessageProducts.ChurchReport/Middleware/SessionValidationMiddleware.cs`: replace sync-over-async session commit with awaited async helper.
- `SpeechMessageProducts.ChurchReport/SessionAttribute.cs`: remove retained `SessionId` state and `async override void`.
- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`: centralize session cache creation, make it atomic, set bounded expirations, and dispose evicted disposable values.
- `LineMessagingProcessor/LineMessagingProcessorClass.cs`: make LINE client ownership explicit and dispose internally owned clients.
- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`: add async notification API; keep sync wrapper only for compatibility; ensure DI path uses injected workflow.
- `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs`: add async API and route DI/runtime call sites away from blocking static defaults.
- `ToolUtility/PushUtility.cs`: keep async methods as primary surface and remove request-path usage of obsolete sync wrappers.
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`: route post-payment workflow through async execution and use injected LINE workflow/client where available.
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`: remove MVC `Controller` inheritance hazard by replacing `Json` and `RedirectToAction` dependencies with explicit result factories; use injected LINE client/workflows.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`: enforce object-level authorization before returning contact images and make private image cache keys user-aware where needed.
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`: verify existing image auth still passes after shared helper changes.
- `SpeechMessageProducts.ChurchReport/Startup.cs`: register any new helper service only when existing DI registrations do not already cover it.
- `.ccg/tasks/full-code-quality-audit-and-fix/task.json`: update task phase through implementation, review, and completion.

Review but only change when a confirmed defect is found:

- `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/*.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/ListManagementController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/*.cs`
- `ToolUtility/ConnectionOperations/CrmConnectionPool.cs`
- `ToolUtility/ListOperations/*.cs`
- `ToolUtility/EntityOperations/*.cs`
- `SpeechMessageProducts.ChurchReport/appsettings.json`

## Safety Gates

- Before each implementation batch, run `git status --short` and do not overwrite unrelated user changes.
- Keep source files in their existing encoding and line ending style.
- Do not set a global `MemoryCacheOptions.SizeLimit` unless every shared `IMemoryCache` entry in the app supplies `Size`. Current image caches already set `Size`, but many other entries do not.
- Do not remove compatibility constructors from public SDK projects unless all tests and project references prove there are no external callers.
- Do not claim memory, socket, or CRM performance improvement without either static proof or runtime measurements named in this plan.

---

## Task 1: Baseline Static Inventory

**Files:**
- Modify: `.ccg/tasks/full-code-quality-audit-and-fix/context.jsonl`
- Read only: entire solution

- [ ] **Step 1: Record branch and dirty state**

Run:

```powershell
Get-Location
git branch --show-current
git status --short
```

Expected:

```text
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree
1.0.0.0.Initialization.Worktree
```

`git status --short` may show current planning artifacts. Treat any unrelated product-code changes as user-owned.

- [ ] **Step 2: Capture risky pattern scan**

Run:

```powershell
rg -n "new HttpClient|new LineMessagingClient|new RestClient|\.GetAwaiter\(\)\.GetResult\(\)|\.Wait\(|\.Result|Task\.Run|Thread\.Sleep" -g "*.cs"
rg -n "SessionId =|async override void|PostEvictionCallbacks|RegisterPostEvictionCallback" -g "*.cs"
rg -n "GetContactImage|GetContactImagesBatch|CanViewContact|AllowAnonymous|Authorize" "SpeechMessageProducts.ChurchReport\Controllers" -g "*.cs"
```

Expected: the known matches remain visible before implementation, including `SessionAttribute.cs`, `SessionValidationMiddleware.cs`, `InMemoryDataContextSmallGroup.cs`, ChurchReport LINE utility constructors, and payment/admin notification sync wrappers.

- [ ] **Step 3: Write context references**

Append one JSON object per important file to `.ccg/tasks/full-code-quality-audit-and-fix/context.jsonl`:

```jsonl
{"path":"SpeechMessageProducts.ChurchReport/Middleware/SessionValidationMiddleware.cs","reason":"sync-over-async session commit"}
{"path":"SpeechMessageProducts.ChurchReport/SessionAttribute.cs","reason":"stateful action filter dead code"}
{"path":"SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs","reason":"per-session cache lifetime and eviction cleanup"}
{"path":"LineMessagingProcessor/LineMessagingProcessorClass.cs","reason":"LINE client ownership"}
{"path":"SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs","reason":"cached business object inherits Controller and owns LINE client"}
{"path":"SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs","reason":"contact image object-level authorization"}
```

- [ ] **Step 4: Commit checkpoint only if user has approved commits**

Run only when commits are part of the execution agreement:

```powershell
git add .ccg/tasks/full-code-quality-audit-and-fix/context.jsonl
git commit -m "chore: record audit context inventory"
```

---

## Task 2: Make SessionValidationMiddleware Fully Async

**Files:**
- Create: `ChurchReport.MemberInfo.Tests/Security/SessionValidationMiddlewareTests.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Middleware/SessionValidationMiddleware.cs`

- [ ] **Step 1: Write the failing test**

Create `ChurchReport.MemberInfo.Tests/Security/SessionValidationMiddlewareTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

public sealed class SessionValidationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_UserAgentMismatch_CommitsSessionAsyncAndRedirects()
    {
        var session = new RecordingSession();
        session.SetString("_SessionUserId", "user-1");
        session.SetString("_SessionUserAgent", "OriginalAgent");

        var context = new DefaultHttpContext();
        context.Request.Path = "/Protected/Page";
        context.Request.Headers["User-Agent"] = "DifferentAgent";
        context.Features.Set<ISessionFeature>(new TestSessionFeature { Session = session });

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<SessionValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        session.Cleared.Should().BeTrue();
        session.CommitCount.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be("/Login");
    }

    private sealed class TestSessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = default!;
    }

    private sealed class RecordingSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public bool IsAvailable => true;
        public string Id { get; } = "session-1";
        public IEnumerable<string> Keys => _values.Keys;
        public bool Cleared { get; private set; }
        public int CommitCount { get; private set; }

        public void Clear()
        {
            Cleared = true;
            _values.Clear();
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _values.Remove(key);

        public void Set(string key, byte[] value) => _values[key] = value;

        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }
}
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~SessionValidationMiddlewareTests"
```

Expected: FAIL or compile failure because the middleware still calls the private sync helper.

- [ ] **Step 3: Replace the sync helper**

In `SpeechMessageProducts.ChurchReport/Middleware/SessionValidationMiddleware.cs`, replace:

```csharp
ClearSessionAndRedirectToLogin(context);
return;
```

with:

```csharp
await ClearSessionAndRedirectToLoginAsync(context);
return;
```

Replace the helper with:

```csharp
private async Task ClearSessionAndRedirectToLoginAsync(HttpContext context)
{
    try
    {
        context.Session.Clear();
        await context.Session.CommitAsync();

        _logger.LogInformation("[Session Validation] Session cleared and committed.");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[Session Validation] Failed to clear session.");
    }

    context.Response.Redirect("/Login");
}
```

- [ ] **Step 4: Verify no blocking commit remains in this file**

Run:

```powershell
rg -n "CommitAsync\(\)\.GetAwaiter\(\)\.GetResult|ClearSessionAndRedirectToLogin\(" SpeechMessageProducts.ChurchReport\Middleware\SessionValidationMiddleware.cs
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~SessionValidationMiddlewareTests"
```

Expected: no `GetResult` match; the test passes.

- [ ] **Step 5: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport\Middleware\SessionValidationMiddleware.cs ChurchReport.MemberInfo.Tests\Security\SessionValidationMiddlewareTests.cs
git commit -m "fix(session): await session commit during validation redirect"
```

---

## Task 3: Neutralize Stateful CheckSessionOutAttribute

**Files:**
- Create: `ChurchReport.MemberInfo.Tests/Security/CheckSessionOutAttributeTests.cs`
- Modify: `SpeechMessageProducts.ChurchReport/SessionAttribute.cs`

- [ ] **Step 1: Write the failing test**

Create `ChurchReport.MemberInfo.Tests/Security/CheckSessionOutAttributeTests.cs`:

```csharp
using System.Linq;
using System.Reflection;
using ChurchReport;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

public sealed class CheckSessionOutAttributeTests
{
    [Fact]
    public void CheckSessionOutAttribute_HasNoDeclaredInstanceFields()
    {
        typeof(CheckSessionOutAttribute)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void CheckSessionOutAttribute_OnActionExecuting_IsSynchronousVoidOverride()
    {
        var method = typeof(CheckSessionOutAttribute).GetMethod(
            "OnActionExecuting",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
        method.GetCustomAttributes(false).Select(a => a.GetType().Name).Should().NotContain("AsyncStateMachineAttribute");
    }
}
```

- [ ] **Step 2: Run the failing test**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~CheckSessionOutAttributeTests"
```

Expected: FAIL because `SessionId` is a declared instance field and the override is `async void`.

- [ ] **Step 3: Replace the attribute body**

In `SpeechMessageProducts.ChurchReport/SessionAttribute.cs`, replace the full `CheckSessionOutAttribute` class with:

```csharp
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public class CheckSessionOutAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        base.OnActionExecuting(filterContext);
    }
}
```

Keep the class name for binary/source compatibility. The attribute becomes a no-op because no current route should depend on its unsafe retained state.

- [ ] **Step 4: Confirm no usage depends on the old behavior**

Run:

```powershell
rg -n "CheckSessionOutAttribute|CheckSessionOut" -g "*.cs"
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~CheckSessionOutAttributeTests"
```

Expected: references are the class and tests only; tests pass.

- [ ] **Step 5: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport\SessionAttribute.cs ChurchReport.MemberInfo.Tests\Security\CheckSessionOutAttributeTests.cs
git commit -m "fix(session): remove retained state from legacy session attribute"
```

---

## Task 4: Centralize Per-Session Memory Cache Creation And Eviction Cleanup

**Files:**
- Create: `ChurchReport.MemberInfo.Tests/Caching/InMemoryDataContextSmallGroupCacheTests.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`

- [ ] **Step 1: Write helper-level tests**

Create `ChurchReport.MemberInfo.Tests/Caching/InMemoryDataContextSmallGroupCacheTests.cs`:

```csharp
using System;
using System.Threading;
using ChurchReport.Models;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Caching;

public sealed class InMemoryDataContextSmallGroupCacheTests
{
    [Fact]
    public void CreateSessionCacheEntryOptions_UsesBoundedExpiration()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var entry = cache.CreateEntry("key");

        InMemoryDataContextSmallGroup.ApplySessionCachePolicyForTesting(entry);

        entry.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(30));
        entry.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(30));
        entry.PostEvictionCallbacks.Should().NotBeEmpty();
    }

    [Fact]
    public void DisposeCachedValueForTesting_DisposesDisposableValues()
    {
        var disposable = new RecordingDisposable();

        InMemoryDataContextSmallGroup.DisposeCachedValueForTesting("key", disposable, EvictionReason.Removed, null);

        disposable.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void DisposeCachedValueForTesting_IgnoresNonDisposableValues()
    {
        var action = () => InMemoryDataContextSmallGroup.DisposeCachedValueForTesting("key", new object(), EvictionReason.Removed, null);

        action.Should().NotThrow();
    }

    private sealed class RecordingDisposable : IDisposable
    {
        private int _disposeCount;
        public int DisposeCount => _disposeCount;
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
```

- [ ] **Step 2: Run the failing test**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~InMemoryDataContextSmallGroupCacheTests"
```

Expected: FAIL to compile because the testing hooks do not exist.

- [ ] **Step 3: Add cache helper methods**

In `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`, add these members inside `InMemoryDataContextSmallGroup`:

```csharp
private static readonly TimeSpan SessionCacheAbsoluteExpiration = TimeSpan.FromMinutes(30);
private static readonly TimeSpan SessionCacheSlidingExpiration = TimeSpan.FromMinutes(30);

private T GetOrCreateSessionCacheValue<T>(string suffix, Func<T> factory)
    where T : class
{
    if (string.IsNullOrWhiteSpace(suffix))
    {
        throw new ArgumentException("Cache suffix is required.", nameof(suffix));
    }

    if (factory == null)
    {
        throw new ArgumentNullException(nameof(factory));
    }

    var key = GetCurrentSessionId() + suffix;
    return _memoryCache.GetOrCreate(key, entry =>
    {
        ApplySessionCachePolicy(entry);
        var value = factory();
        SetSessionDirtyFlag();
        return value;
    });
}

private static void ApplySessionCachePolicy(ICacheEntry entry)
{
    entry.AbsoluteExpirationRelativeToNow = SessionCacheAbsoluteExpiration;
    entry.SlidingExpiration = SessionCacheSlidingExpiration;
    entry.RegisterPostEvictionCallback(DisposeCachedValue);
}

private static void DisposeCachedValue(object key, object value, EvictionReason reason, object state)
{
    try
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[InMemoryDataContextSmallGroup] Cache eviction cleanup failed for {key}: {ex.Message}");
    }
}

internal static void ApplySessionCachePolicyForTesting(ICacheEntry entry)
{
    ApplySessionCachePolicy(entry);
}

internal static void DisposeCachedValueForTesting(object key, object value, EvictionReason reason, object state)
{
    DisposeCachedValue(key, value, reason, state);
}
```

Add to `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj` if internals are not already visible to the test project:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="ChurchReport.MemberInfo.Tests" />
</ItemGroup>
```

If the project already has `InternalsVisibleTo`, append only the missing test assembly.

- [ ] **Step 4: Replace repeated cache blocks**

In `InMemoryDataContextSmallGroup.cs`, replace each `if (_memoryCache.Get(key) == null) { ... _memoryCache.Set(...); SetSessionDirtyFlag(); } return _memoryCache.Get<T>(key);` property body with a `GetOrCreateSessionCacheValue` call. Use this exact mapping:

```csharp
public ListManager ListManager =>
    GetOrCreateSessionCacheValue("_ListManager", () => m_ListManager = new ListManager());

public SmallGroupDataList SmallGroupDataList =>
    GetOrCreateSessionCacheValue("_SmallGroupDataList", () => m_SmallGroupDataList = new SmallGroupDataList());

public WeeklyReportData WeeklyReportData =>
    GetOrCreateSessionCacheValue("_WeeklyReportData", () => m_WeeklyReportData = new WeeklyReportData());

public NewPersonModel NewPersonModel =>
    GetOrCreateSessionCacheValue("_NewPersonModel", () => m_NewPersonModel = new NewPersonModel());

public PersonalInfomationModel PersonalInfomationModel =>
    GetOrCreateSessionCacheValue("_PersonalInfomationModel", () => m_PersonalInfomationModel = new PersonalInfomationModel());

public HappyGroupDataManager HappyGroupDataManager =>
    GetOrCreateSessionCacheValue("_HappyGroupDataManager", () => m_HappyGroupDataManager = new HappyGroupDataManager());

public ListManagementDataManager ListManagementDataManager =>
    GetOrCreateSessionCacheValue("_ListManagementDataManager", () => m_ListManagementDataManager = new ListManagementDataManager());

public EquipmentDataManager EquipmentDataManager =>
    GetOrCreateSessionCacheValue("_EquipmentDataManager", () => m_EquipmentDataManager = new EquipmentDataManager());

public FeeList FeeList =>
    GetOrCreateSessionCacheValue("_FeeList", () => m_FeeList = new FeeList());

public AppointmentsListManager AppointmentsListManager =>
    GetOrCreateSessionCacheValue("_AppointmentsListManager", () => m_AppointmentsListManager = new AppointmentsListManager());

public DonationPaymentManager DonationPaymentManager =>
    GetOrCreateSessionCacheValue("_DonationPaymentManager", () =>
        m_DonationPaymentManager = new DonationPaymentManager(
            m_DonationPaymentCreateGatewayAdapter,
            _lineNotificationWorkflow,
            _lineReplyWorkflow));

public PollManager PollManager =>
    GetOrCreateSessionCacheValue("_PollManager", () => m_PollManager = new PollManager());

public ToolUtilityClass ToolUtilityClass =>
    GetOrCreateSessionCacheValue("_ToolUtilityClass", () => m_ToolUtilityClass = _toolUtilityProvider.GetToolUtility());
```

Before applying each replacement, confirm the property name and backing field exist in the file. If a constructor argument is required by current code, preserve the current factory expression while still using the helper.

- [ ] **Step 5: Run cache tests and static scan**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~InMemoryDataContextSmallGroupCacheTests"
rg -n "PostEvictionCallbacks|_memoryCache\.Get\(key\) == null|SetAbsoluteExpiration\(DateTime\.Now" SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs
```

Expected: tests pass; scan has no old callback blocks or double-read cache pattern in this file.

- [ ] **Step 6: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj ChurchReport.MemberInfo.Tests\Caching\InMemoryDataContextSmallGroupCacheTests.cs
git commit -m "fix(cache): centralize session cache eviction cleanup"
```

---

## Task 5: Make LINE Client Ownership Explicit

**Files:**
- Modify: `LineMessagingProcessor/LineMessagingProcessorClass.cs`
- Modify: `LineMessagingProcessor.Tests/LineMessagingProcessorSendMessageTests.cs`
- Create: `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineClientOwnershipScanTests.cs`

- [ ] **Step 1: Write ownership scan test**

Create `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineClientOwnershipScanTests.cs`:

```csharp
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class LineClientOwnershipScanTests
{
    [Fact]
    public void ChurchReportProductionCode_DoesNotUseTokenOnlyLineMessagingClientConstructor()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "SpeechMessageProducts.ChurchReport"),
            "*.cs",
            SearchOption.AllDirectories);

        var offenders = files
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { path, line, number = index + 1 }))
            .Where(hit => hit.line.Contains("new LineMessagingClient(") &&
                          !hit.line.Contains("new LineMessagingClient(httpClient") &&
                          !hit.line.Contains("new LineMessagingClient(lineHttpClient") &&
                          !hit.line.Contains("new LineMessagingClient(m_LineMessagingClient"))
            .Select(hit => $"{Path.GetRelativePath(root, hit.path)}:{hit.number}:{hit.line.Trim()}")
            .ToArray();

        offenders.Should().BeEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return directory!.FullName;
    }
}
```

- [ ] **Step 2: Run the failing scan test**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineClientOwnershipScanTests"
```

Expected: FAIL with current ChurchReport direct `new LineMessagingClient(channelAccessToken)` locations.

- [ ] **Step 3: Track ownership in LineMessagingProcessorClass**

In `LineMessagingProcessor/LineMessagingProcessorClass.cs`, add:

```csharp
private readonly bool _ownsLineMessagingClient;
```

Set it in constructors:

```csharp
public LineMessagingProcessorClass(string channelAccessToken)
{
    _channelAccessToken = NormalizeBearerToken(channelAccessToken);
    _requiresChannelAccessToken = true;
#pragma warning disable CS0618
    _lineMessagingClient = new LineMessagingClient(StripBearerPrefix(_channelAccessToken));
#pragma warning restore CS0618
    _ownsLineMessagingClient = true;
}

public LineMessagingProcessorClass(LineMessagingClient lineMessagingClient)
{
    _lineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
    _channelAccessToken = string.Empty;
    _requiresChannelAccessToken = false;
    _ownsLineMessagingClient = false;
}
```

Update `Dispose(bool disposing)`:

```csharp
if (disposing && _ownsLineMessagingClient)
{
    _lineMessagingClient.Dispose();
}
```

- [ ] **Step 4: Add processor disposal regression**

Append to `LineMessagingProcessor.Tests/LineMessagingProcessorSendMessageTests.cs`:

```csharp
[Fact]
public void Dispose_CanBeCalledRepeatedly_WhenProcessorOwnsCompatibilityClient()
{
    using var processor = new LineMessagingProcessorClass("test-token");

    processor.Dispose();
    processor.Dispose();
}
```

- [ ] **Step 5: Run processor tests**

```powershell
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit checkpoint only if user has approved commits**

```powershell
git add LineMessagingProcessor\LineMessagingProcessorClass.cs LineMessagingProcessor.Tests\LineMessagingProcessorSendMessageTests.cs ChurchReport.MemberInfo.Tests\LineSharedWorkflow\LineClientOwnershipScanTests.cs
git commit -m "fix(line): make processor client ownership explicit"
```

---

## Task 6: Route ChurchReport LINE Call Sites Through DI Or Explicit Client Injection

**Files:**
- Modify: `SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
- Modify: `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs`
- Modify: `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Startup.cs`
- Test: `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineClientOwnershipScanTests.cs`

- [ ] **Step 1: Use existing DI registration as the source of LINE clients**

Confirm `Startup.cs` already calls:

```csharp
services.AddLineMessagingProcessor(options =>
{
    var defaultOrg = Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
    options.ChannelAccessToken =
        Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"] ??
        Configuration["LINE_CHANNEL_ACCESS_TOKEN"] ??
        string.Empty;
});
```

Expected: this exists and registers `LineMessagingClient`, `LineMessagingProcessorClass`, `ILineNotificationWorkflow`, and `ILineReplyWorkflow`.

- [ ] **Step 2: Add overloads that accept LineMessagingClient**

For each utility that currently has only a token constructor, add an overload with this pattern:

```csharp
private readonly LineMessagingClient m_LineMessagingClient;

public QrCodeUtility(LineMessagingClient lineMessagingClient)
{
    m_LineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
}
```

Keep the existing string constructor only when non-DI callers still compile, and mark it as compatibility:

```csharp
[Obsolete("Use the constructor that accepts LineMessagingClient from DI.")]
public QrCodeUtility(string channelAccessToken)
    : this(new LineMessagingClient(new HttpClient(), channelAccessToken))
{
}
```

This compatibility constructor is a temporary public API bridge; production ChurchReport call sites must not use it. If the class implements `IDisposable`, dispose only clients it created itself. If it does not implement `IDisposable`, do not create an owned `HttpClient` inside production paths.

- [ ] **Step 3: Replace ChurchReport production call sites**

For controllers and scoped services that create these utilities, request `LineMessagingClient`, `ILineNotificationWorkflow`, or `ILineReplyWorkflow` from constructor injection and pass those dependencies down. Use existing workflow types first:

```csharp
private readonly LineMessagingClient _lineMessagingClient;
private readonly ILineNotificationWorkflow _lineNotificationWorkflow;
private readonly ILineReplyWorkflow _lineReplyWorkflow;

public SomeController(
    LineMessagingClient lineMessagingClient,
    ILineNotificationWorkflow lineNotificationWorkflow,
    ILineReplyWorkflow lineReplyWorkflow)
{
    _lineMessagingClient = lineMessagingClient ?? throw new ArgumentNullException(nameof(lineMessagingClient));
    _lineNotificationWorkflow = lineNotificationWorkflow ?? throw new ArgumentNullException(nameof(lineNotificationWorkflow));
    _lineReplyWorkflow = lineReplyWorkflow ?? throw new ArgumentNullException(nameof(lineReplyWorkflow));
}
```

For `InMemoryDataContextSmallGroup`, extend the constructor with optional `LineMessagingClient lineMessagingClient = null` only if `DonationPaymentManager` still requires a direct client after Task 7. Preserve existing optional parameters so current DI registration keeps compiling.

- [ ] **Step 4: Run the scan and targeted tests**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineClientOwnershipScanTests"
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow"
rg -n "new LineMessagingClient\(" SpeechMessageProducts.ChurchReport -g "*.cs"
```

Expected: scan test passes; `rg` has no production ChurchReport `new LineMessagingClient(token)` call sites. Allowed remaining matches are tests and compatibility constructors outside ChurchReport request paths.

- [ ] **Step 5: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport ChurchReport.MemberInfo.Tests\LineSharedWorkflow\LineClientOwnershipScanTests.cs
git commit -m "fix(line): route ChurchReport LINE clients through DI"
```

---

## Task 7: Remove DonationPaymentManager Controller Lifetime Hazard

**Files:**
- Modify: `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Services/DonationContactCreationService.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Services/DonationKeyInDedicationService.cs`
- Modify: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs`

- [ ] **Step 1: Add a regression test that DonationPaymentManager is not an MVC Controller**

Append to `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs`:

```csharp
[Fact]
public void DonationPaymentManager_DoesNotInheritMvcController()
{
    typeof(ChurchReport.Models.DonationPaymentManager)
        .Should()
        .NotBeAssignableTo<Microsoft.AspNetCore.Mvc.Controller>();
}
```

- [ ] **Step 2: Run the failing test**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentManager_DoesNotInheritMvcController"
```

Expected: FAIL because the class currently inherits `Controller`.

- [ ] **Step 3: Replace Controller helpers with explicit factories**

In `DonationPaymentManager.cs`, remove `: Controller` from the class declaration.

Add static result helpers:

```csharp
private static JsonResult JsonResult(object value)
{
    return new JsonResult(value);
}

private static RedirectToActionResult RedirectToActionResult(string actionName, object routeValues)
{
    return new RedirectToActionResult(actionName, "Home", routeValues);
}
```

Replace constructor arguments:

```csharp
m_DonationKeyInDedicationService = new DonationKeyInDedicationService(
    m_ToolUtilityClass,
    m_DonationPaymentFormModel,
    m_DonationPaymentProcessor,
    JsonResult,
    NotifyDonationPaymentError);

m_DonationContactCreationService = new DonationContactCreationService(
    m_ToolUtilityClass,
    JsonResult,
    RedirectToActionResult,
    NotifyDonationRegistrationError);
```

Replace direct `Json(...)` calls in `SaveDonationPaymentDedicationAsync` with `JsonResult(...)`.

- [ ] **Step 4: Keep result factory contracts explicit**

In `DonationContactCreationService.cs` and `DonationKeyInDedicationService.cs`, keep constructor parameters as:

```csharp
Func<object, JsonResult> json
Func<string, object, RedirectToActionResult> redirectToAction
```

No service should depend on `Controller`, `ControllerBase`, or `HttpContext`.

- [ ] **Step 5: Run payment tests**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments"
```

Expected: PASS.

- [ ] **Step 6: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport\Models\DonationPaymentManager.cs SpeechMessageProducts.ChurchReport\Services\DonationContactCreationService.cs SpeechMessageProducts.ChurchReport\Services\DonationKeyInDedicationService.cs ChurchReport.MemberInfo.Tests\Payments
git commit -m "fix(payments): remove controller lifetime from donation manager"
```

---

## Task 8: Add Async Notification APIs And Remove Request-Path Blocking

**Files:**
- Modify: `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- Modify: `ToolUtility/PushUtility.cs`
- Modify: `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/ChurchReportLineAdminNotificationServiceTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs`

- [ ] **Step 1: Add async payment notification tests**

Append to `PaymentNotificationServiceWorkflowTests.cs`:

```csharp
[Fact]
public async Task SendLineMessageAsync_uses_shared_workflow_with_retry_key()
{
    var workflow = new CapturingWorkflow();
    var service = new PaymentNotificationService(
        NullLogger<PaymentNotificationService>.Instance,
        new PaymentMessageBuilder(),
        new PaymentFeeTypeHelper(NullLogger<PaymentFeeTypeHelper>.Instance),
        workflow);

    await service.SendLineMessageAsync("Udonor", "payment received", "retry-001");

    workflow.Requests.Should().ContainSingle();
    workflow.Requests[0].RetryKey.Should().Be("retry-001");
}
```

- [ ] **Step 2: Implement async API and keep compatibility wrapper**

In `PaymentNotificationService.cs`, add:

```csharp
public Task SendLineMessageAsync(string lineId, string message)
{
    return SendLineMessageAsync(lineId, message, retryKey: null);
}

public async Task SendLineMessageAsync(string lineId, string message, string? retryKey)
{
    try
    {
        var request = new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(lineId),
            Content = LineNotificationContent.TextMessage(message),
            RetryKey = retryKey,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "ChurchReport.PaymentNotificationService"
            }
        };

        await _lineNotificationWorkflow.SendOrThrowAsync(request);
        _logger.LogInformation("SendLineMessageAsync succeeded. RetryKey: {RetryKey}", retryKey ?? "<none>");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "SendLineMessageAsync failed. RetryKey: {RetryKey}", retryKey ?? "<none>");
        throw;
    }
}
```

Change existing sync wrappers to:

```csharp
public void SendLineMessage(string lineId, string message)
{
    SendLineMessage(lineId, message, retryKey: null);
}

public void SendLineMessage(string lineId, string message, string? retryKey)
{
    SendLineMessageAsync(lineId, message, retryKey).GetAwaiter().GetResult();
}
```

The wrapper remains for legacy non-request callers. Request-path callers must use `SendLineMessageAsync`.

- [ ] **Step 3: Add async admin notification API**

In `ChurchReportLineAdminNotificationService.cs`, add:

```csharp
public static Task NotifyDefaultErrorAsync(string source, string errorMessage)
{
    return s_default.Value.NotifyErrorAsync(source, errorMessage);
}

public static Task NotifyDefaultErrorAsync(string source, string category, string errorMessage)
{
    return s_default.Value.NotifyErrorAsync(source, category, errorMessage);
}

public Task NotifyErrorAsync(string source, string errorMessage)
{
    return NotifyErrorAsync(source, DefaultCategory, errorMessage);
}

public async Task NotifyErrorAsync(string source, string category, string errorMessage)
{
    try
    {
        var normalizedSource = Normalize(source, DefaultProductSource);
        var normalizedCategory = Normalize(category, DefaultCategory);
        var message = FormatAdminMessage(normalizedSource, normalizedCategory, errorMessage);

        await _lineNotificationWorkflow.SendAsync(new LineNotificationRequest
        {
            Recipient = LineNotificationRecipient.User(_adminLineUserId),
            Content = LineNotificationContent.TextMessage(message),
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "ChurchReport.LineAdminErrorNotification",
                ["productSource"] = normalizedSource,
                ["category"] = normalizedCategory
            }
        });
    }
    catch
    {
    }
}
```

Keep existing sync methods as wrappers:

```csharp
public void NotifyError(string source, string category, string errorMessage)
{
    NotifyErrorAsync(source, category, errorMessage).GetAwaiter().GetResult();
}
```

- [ ] **Step 4: Convert request-path callers**

In `DonationFeePaymentProcessor.cs`, change `ExecutePostPaymentWorkflowIfAvailable` to:

```csharp
private async Task ExecutePostPaymentWorkflowIfAvailableAsync(
    Entity feeEntity,
    PaymentWorkflowResult workflowResult,
    bool isPaymentSuccess)
{
    if (feeEntity == null || workflowResult == null || m_PaymentContextBuilder == null)
    {
        return;
    }

    var context = m_PaymentContextBuilder.Build(
        m_ToolUtilityClass,
        feeEntity,
        workflowResult,
        isPaymentSuccess);

    await m_PostPaymentWorkflow.ExecuteAsync(context);
}
```

At each caller inside async methods, replace:

```csharp
ExecutePostPaymentWorkflowIfAvailable(aFeeEntity, workflowResult, isPaymentSuccess);
```

with:

```csharp
await ExecutePostPaymentWorkflowIfAvailableAsync(aFeeEntity, workflowResult, isPaymentSuccess);
```

- [ ] **Step 5: Verify remaining blocking wrappers are not request-path calls**

Run:

```powershell
rg -n "\.GetAwaiter\(\)\.GetResult\(\)|\.Wait\(|\.Result" SpeechMessageProducts.ChurchReport ToolUtility -g "*.cs"
```

Expected: remaining matches are either tests, compatibility wrappers, plain properties named `Result`, or documented non-request code. Every remaining production match must be recorded in the final report with reason.

- [ ] **Step 6: Run targeted tests**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow"
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Payments"
```

Expected: PASS.

- [ ] **Step 7: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport\Services\PaymentNotificationService.cs SpeechMessageProducts.ChurchReport\Services\ChurchReportLineAdminNotificationService.cs SpeechMessageProducts.ChurchReport\Tools\DonationFeePaymentProcessor.cs ToolUtility\PushUtility.cs ChurchReport.MemberInfo.Tests\LineSharedWorkflow
git commit -m "fix(async): add async notification paths and remove payment blocking"
```

---

## Task 9: Enforce Personal Image Object-Level Authorization

**Files:**
- Create: `ChurchReport.MemberInfo.Tests/Security/PersonalImageAuthorizationTests.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`
- Review: `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`

- [ ] **Step 1: Add helper contract tests**

Create `ChurchReport.MemberInfo.Tests/Security/PersonalImageAuthorizationTests.cs`:

```csharp
using System;
using ChurchReport.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

public sealed class PersonalImageAuthorizationTests
{
    [Fact]
    public void CanViewPersonalContactImage_AllowsLoginContact()
    {
        var loginContactId = Guid.NewGuid();
        var loginContact = new Entity("contact", loginContactId);

        PersonalController.CanViewPersonalContactImageForTesting(loginContact, loginContactId)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void CanViewPersonalContactImage_DeniesDifferentContact()
    {
        var loginContact = new Entity("contact", Guid.NewGuid());

        PersonalController.CanViewPersonalContactImageForTesting(loginContact, Guid.NewGuid())
            .Should()
            .BeFalse();
    }

    [Fact]
    public void BuildPersonalContactImageCacheKey_IncludesViewerContact()
    {
        var viewerId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var key = PersonalController.BuildPersonalContactImageCacheKeyForTesting(viewerId, contactId, 80);

        key.Should().Contain(viewerId.ToString("N"));
        key.Should().Contain(contactId.ToString("N"));
        key.Should().Contain(":80");
    }
}
```

- [ ] **Step 2: Run the failing tests**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~PersonalImageAuthorizationTests"
```

Expected: FAIL to compile because test hooks do not exist.

- [ ] **Step 3: Add Personal image authorization helpers**

In `PersonalController.ImageUpload.cs`, add:

```csharp
private static bool CanViewPersonalContactImage(Entity loginContact, Guid requestedContactId)
{
    return loginContact != null && loginContact.Id == requestedContactId;
}

private static string BuildPersonalContactImageCacheKey(Guid viewerContactId, Guid requestedContactId, int thumbSize)
{
    return $"personal-contact-image-thumb:{viewerContactId:N}:{requestedContactId:N}:{thumbSize}";
}

internal static bool CanViewPersonalContactImageForTesting(Entity loginContact, Guid requestedContactId)
{
    return CanViewPersonalContactImage(loginContact, requestedContactId);
}

internal static string BuildPersonalContactImageCacheKeyForTesting(Guid viewerContactId, Guid requestedContactId, int thumbSize)
{
    return BuildPersonalContactImageCacheKey(viewerContactId, requestedContactId, thumbSize);
}
```

- [ ] **Step 4: Apply authorization in GetContactImage**

In `GetContactImage`, after `contactGuid` is resolved and before cache lookup or CRM retrieve:

```csharp
var loginContactForAuth = InMemoryContext?.PersonalInfomationModel?.m_LoginContact;
if (!CanViewPersonalContactImage(loginContactForAuth, contactGuid))
{
    Response.Headers["Cache-Control"] = "private, no-store";
    return StatusCode(StatusCodes.Status403Forbidden);
}

var viewerContactId = loginContactForAuth.Id;
```

Replace the cache key with:

```csharp
var cacheKey = returnOriginal
    ? $"personal-contact-image-full:{viewerContactId:N}:{contactGuid:N}"
    : BuildPersonalContactImageCacheKey(viewerContactId, contactGuid, thumbSize);
```

- [ ] **Step 5: Apply authorization in GetContactImagesBatch**

At the start of `GetContactImagesBatch`, after validating `request.ContactIds`:

```csharp
var loginContactForAuth = InMemoryContext?.PersonalInfomationModel?.m_LoginContact;
if (loginContactForAuth == null)
{
    Response.Headers["Cache-Control"] = "private, no-store";
    return StatusCode(StatusCodes.Status403Forbidden);
}

var viewerContactId = loginContactForAuth.Id;
```

While iterating `request.ContactIds`, skip or reject unauthorized contacts. For Personal endpoints, use strict same-contact behavior:

```csharp
if (!CanViewPersonalContactImage(loginContactForAuth, guid))
{
    Response.Headers["Cache-Control"] = "private, no-store";
    return StatusCode(StatusCodes.Status403Forbidden);
}
```

Use `BuildPersonalContactImageCacheKey(viewerContactId, guid, thumbSize)` for batch cache keys.

- [ ] **Step 6: Verify MemberInfo image endpoint still has its broader access rules**

Run:

```powershell
rg -n "CanViewContact|GetContactImage|GetContactImagesBatch" SpeechMessageProducts.ChurchReport\Controllers\MemberInfoController.cs
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~MemberInfoAccessResolverTests|FullyQualifiedName~PersonalImageAuthorizationTests"
```

Expected: MemberInfo keeps its existing scoped access checks; Personal image tests pass.

- [ ] **Step 7: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport\Controllers\PersonalController.ImageUpload.cs ChurchReport.MemberInfo.Tests\Security\PersonalImageAuthorizationTests.cs
git commit -m "fix(auth): enforce personal contact image ownership"
```

---

## Task 10: API Controller Authorization And Query Performance Audit

**Files:**
- Review and modify if needed: `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/*.cs`
- Review and modify if needed: `ToolUtility/ConnectionOperations/CrmConnectionPool.cs`
- Review and modify if needed: `ToolUtility/ListOperations/*.cs`
- Review and modify if needed: `ToolUtility/EntityOperations/*.cs`
- Modify: `.ccg/tasks/full-code-quality-audit-and-fix/review.md`

- [ ] **Step 1: List API controllers**

Run:

```powershell
rg --files SpeechMessageProducts.ChurchReport\Controllers\ApiControllers
```

Expected: files under `Controllers\ApiControllers`, not a root `ApiControllers` folder.

- [ ] **Step 2: Scan authorization markers and session identity use**

Run:

```powershell
rg -n "class .*Controller|HttpGet|HttpPost|Route|AllowAnonymous|Authorize|Session|_SessionUserId|User\.Identity|CanView" SpeechMessageProducts.ChurchReport\Controllers\ApiControllers -g "*.cs"
```

For each endpoint returning contact, group, schedule, shepherd, spirit leader, assignment, or member data, verify one of these is true:

- It is explicitly anonymous and returns only public metadata.
- It requires authenticated session/auth ticket.
- It checks object-level access before returning data.

- [ ] **Step 3: Patch confirmed missing authorization with existing patterns**

Use the existing `GlobalAuthorizationFilter` and `MemberInfoController.CanViewContact` patterns. For an action that requires login, add no `[AllowAnonymous]`. For object access, add a local helper with this shape:

```csharp
private bool CurrentUserCanAccessContact(Guid contactId)
{
    var loginContact = InMemoryContext?.PersonalInfomationModel?.m_LoginContact;
    if (loginContact == null)
    {
        return false;
    }

    return loginContact.Id == contactId;
}
```

Return `Forbid()` for authenticated users without access and `Unauthorized()` for missing identity. Preserve existing JSON shapes when callers expect JSON.

- [ ] **Step 4: Scan CRM query hot spots**

Run:

```powershell
rg -n "new QueryExpression|ColumnSet\(true\)|RetrieveMultiple|TopCount|PageInfo|Task\.Run|Thread\.Sleep" ToolUtility SpeechMessageProducts.ChurchReport -g "*.cs"
```

Patch only confirmed defects:

- Replace `new ColumnSet(true)` in hot endpoints with named columns needed by the response.
- Add `TopCount` or `PageInfo` where a query feeds a dashboard/list and currently has no bound.
- Do not wrap synchronous CRM SDK calls in `Task.Run` unless the action already needs parallel independent CRM reads and no shared `HttpContext`, session, or mutable model is captured.

- [ ] **Step 5: Record audit outcome**

Append to `.ccg/tasks/full-code-quality-audit-and-fix/review.md`:

```markdown
## API And Query Audit

- Controllers inspected:
- Authorization defects fixed:
- Query bounds or ColumnSet fixes:
- Matches intentionally left unchanged:
```

Fill each bullet with concrete file paths and reasons from the scan results.

- [ ] **Step 6: Run affected tests**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~Security|FullyQualifiedName~MemberInfo|FullyQualifiedName~ScopeGuard"
dotnet test ToolUtility.Tests\ToolUtility.Tests.csproj
```

Expected: PASS, or document pre-existing failures with exact test names and first error lines.

- [ ] **Step 7: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport\Controllers\ApiControllers ToolUtility .ccg/tasks/full-code-quality-audit-and-fix/review.md
git commit -m "fix(auth): close api authorization and query hot spots"
```

---

## Task 11: Secrets And Configuration Risk Cleanup

**Files:**
- Review and modify if safe: `SpeechMessageProducts.ChurchReport/appsettings.json`
- Review and modify if safe: `SpeechMessageProducts.ChurchReport/appsettings.Development.json`
- Modify: `.ccg/tasks/full-code-quality-audit-and-fix/review.md`

- [ ] **Step 1: Scan likely secrets**

Run:

```powershell
rg -n "Password|Secret|ChannelAccessToken|AccessToken|ApiKey|ClientSecret|ShopNo|HashKey|HashIV|Token" -g "appsettings*.json" -g "*.cs"
```

- [ ] **Step 2: Remove checked-in active secrets only where env fallback exists**

For each active secret in appsettings, verify code reads an environment variable or user secret fallback. If fallback exists, replace the checked-in value with an empty string:

```json
"Password": "",
"ChannelAccessToken": "",
"ClientSecret": ""
```

If no fallback exists, add environment variable fallback in code with this shape:

```csharp
var password = configuration["CrmConnection:Password"]
               ?? Environment.GetEnvironmentVariable("CRM_PASSWORD");
```

Then blank the checked-in value.

- [ ] **Step 3: Record rotation requirement**

Append to `.ccg/tasks/full-code-quality-audit-and-fix/review.md`:

```markdown
## Secret Rotation Required

The current branch can remove active checked-in secret values from working files, but git history and provider-side credentials require owner action. Rotate each value that was ever committed.
```

List each key name, not the secret value.

- [ ] **Step 4: Build after config changes**

```powershell
dotnet build SpeechMessageProducts.sln
```

Expected: build succeeds. If local build needs private values, set them through environment variables before running build.

- [ ] **Step 5: Commit checkpoint only if user has approved commits**

```powershell
git add SpeechMessageProducts.ChurchReport\appsettings*.json SpeechMessageProducts.ChurchReport\Startup.cs .ccg/tasks/full-code-quality-audit-and-fix/review.md
git commit -m "chore(config): remove active secrets from checked-in settings"
```

---

## Task 12: Static Scan Closure

**Files:**
- Modify: `.ccg/tasks/full-code-quality-audit-and-fix/review.md`

- [ ] **Step 1: Re-run risky pattern scans**

```powershell
rg -n "new HttpClient|new LineMessagingClient|new RestClient|\.GetAwaiter\(\)\.GetResult\(\)|\.Wait\(|\.Result|Task\.Run|Thread\.Sleep" -g "*.cs"
rg -n "SessionId =|async override void|PostEvictionCallbacks|RegisterPostEvictionCallback" -g "*.cs"
rg -n "ColumnSet\(true\)|RetrieveMultiple|TopCount|PageInfo" ToolUtility SpeechMessageProducts.ChurchReport -g "*.cs"
```

- [ ] **Step 2: Classify every remaining match**

Write classifications to `.ccg/tasks/full-code-quality-audit-and-fix/review.md`:

```markdown
## Remaining Static Matches

### Allowed
- `Line.Messaging/LineMessagingClient.cs`: compatibility constructor is public SDK API; production ChurchReport call sites do not use it.
- `*.Tests/*.cs`: tests construct fake `HttpClient` around in-memory handlers.

### Requires owner/runtime decision
- 

### Fixed in this branch
- 
```

The two empty bullets must be filled or removed during execution.

- [ ] **Step 3: Verify git diff scope**

```powershell
git diff --stat
git diff --name-only
```

Expected: changed files match this plan's file map or are explicitly recorded as confirmed defects in `review.md`.

---

## Task 13: Full Build And Test Verification

**Files:**
- Read only unless fixing test failures

- [ ] **Step 1: Build solution**

```powershell
dotnet build SpeechMessageProducts.sln
```

Expected: success.

- [ ] **Step 2: Run targeted test projects**

```powershell
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj
dotnet test ToolUtility.Tests\ToolUtility.Tests.csproj
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
```

Expected: all pass. If a project path does not exist, record that exact path as unavailable and run `rg --files -g "*.csproj" | rg "Tests"` to find the correct test project.

- [ ] **Step 3: Optional runtime proof when app can run locally**

Run app:

```powershell
dotnet run --project SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj
```

In a second shell:

```powershell
dotnet-counters monitor --process-id <PID> System.Runtime Microsoft.AspNetCore.Hosting
```

Exercise login, contact image, QR, payment callback test routes, and LINE notification test doubles. Record:

- GC heap size trend
- threadpool queue length
- request duration
- exception count

If local app cannot run because private CRM/payment credentials are unavailable, record the blocker and keep runtime claims out of the success statement.

---

## Task 14: CCG External Review Through Self-Healing Runner

**Files:**
- Create: `.ccg/dual-model-runs/full-code-quality-audit-and-fix-review-input.md`
- Modify: `.ccg/tasks/full-code-quality-audit-and-fix/review.md`

- [ ] **Step 1: Create review prompt**

Write `.ccg/dual-model-runs/full-code-quality-audit-and-fix-review-input.md`:

```markdown
# Full Code Quality Audit And Fix Review Request

Review the current git diff for defects in:

- Session isolation and cross-user data leakage
- Memory cache lifetime, disposal, and bounded growth
- LINE and HTTP client ownership
- Sync-over-async request paths
- Object-level authorization
- CRM query performance and unbounded reads
- Secret/config handling

Use Critical / Warning / Info severity. Cite exact files and line numbers. Do not suggest broad rewrites unless a concrete defect remains.

Diff under review:

```text
<paste git diff here during execution>
```
```

- [ ] **Step 2: Run CCG review through project runner**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role reviewer `
  -Title "full-code-quality-audit-and-fix-review" `
  -PromptFile ".\.ccg\dual-model-runs\full-code-quality-audit-and-fix-review-input.md" `
  -RepositoryPath "D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

Expected: `ok=true`. If Gemini remains quota blocked and Claude succeeds, record degraded single-model fallback and do not describe it as full dual-model review.

- [ ] **Step 3: Fix Critical findings**

For every Critical finding, add a new subtask in `review.md` with:

```markdown
### Critical Fix: <short title>

- Finding source:
- File:
- Fix:
- Test:
- Result:
```

Implement, test, and rerun the CCG review command.

- [ ] **Step 4: Record Warning decisions**

For each Warning, either fix it or record why it is accepted. Accepted warnings require a concrete reason such as compatibility, missing runtime environment, or separate owner credential rotation.

---

## Task 15: Final Report And Task Closure

**Files:**
- Modify: `.ccg/tasks/full-code-quality-audit-and-fix/task.json`
- Modify: `.ccg/tasks/full-code-quality-audit-and-fix/review.md`
- Optional modify: `.ccg/spec/backend/index.md` if a durable convention was learned and `.ccg/spec` exists

- [ ] **Step 1: Write final technical report**

Append to `.ccg/tasks/full-code-quality-audit-and-fix/review.md`:

```markdown
## Final Repair Report

### Proven Static Fixes
- 

### Tests And Build Evidence
- 

### Remaining Runtime Proof Needed
- 

### CCG Review Status
- 

### Owner Actions
- Rotate any credential value that was present in git history.
```

Fill each bullet with concrete commands and outcomes from execution.

- [ ] **Step 2: Update task phase**

Set `.ccg/tasks/full-code-quality-audit-and-fix/task.json`:

```json
{
  "status": "in_progress",
  "currentPhase": "completed_pending_archive",
  "nextAction": "Archive CCG task after user accepts final repair report"
}
```

Preserve existing `id`, `title`, `complexity`, `risk`, `domain`, `createdAt`, and `branch` fields.

- [ ] **Step 3: CCG archive only after implementation is accepted**

Run only after the final repair is accepted:

```powershell
$archiveMonth = Get-Date -Format "yyyy-MM"
New-Item -ItemType Directory -Force ".ccg\tasks\archive\$archiveMonth"
Move-Item ".ccg\tasks\full-code-quality-audit-and-fix" ".ccg\tasks\archive\$archiveMonth\"
git add .ccg\tasks
git commit -m "chore: archive ccg task full-code-quality-audit-and-fix"
```

---

## Verification Command Summary

Run these before claiming completion:

```powershell
dotnet build SpeechMessageProducts.sln
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj
dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj
dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj
dotnet test ToolUtility.Tests\ToolUtility.Tests.csproj
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
rg -n "new HttpClient|new LineMessagingClient|new RestClient|\.GetAwaiter\(\)\.GetResult\(\)|\.Wait\(|\.Result|Task\.Run|Thread\.Sleep" -g "*.cs"
rg -n "SessionId =|async override void|PostEvictionCallbacks|RegisterPostEvictionCallback" -g "*.cs"
rg -n "GetContactImage|GetContactImagesBatch|CanViewContact|AllowAnonymous|Authorize" "SpeechMessageProducts.ChurchReport\Controllers" -g "*.cs"
git diff --stat
git diff --name-only
```

Runtime verification command when the app and private dependencies are available:

```powershell
dotnet-counters monitor --process-id <PID> System.Runtime Microsoft.AspNetCore.Hosting
```

CCG review command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role reviewer `
  -Title "full-code-quality-audit-and-fix-review" `
  -PromptFile ".\.ccg\dual-model-runs\full-code-quality-audit-and-fix-review-input.md" `
  -RepositoryPath "D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

