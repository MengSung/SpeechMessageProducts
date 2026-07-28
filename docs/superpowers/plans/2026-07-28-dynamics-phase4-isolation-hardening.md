# Dynamics Phase 4 Isolation Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the immediately demonstrable admission and HTTP-session isolation gaps without enabling Dynamics traffic or assuming that the unproven ADFS service-identity flow works.

**Architecture:** Preserve the existing no-SDK boundary and feature-flag-off rollout. Admission receives a local atomic reservation covering both running and queued work; the in-memory coordinator becomes locally atomic but remains explicitly non-durable. ADFS token HTTP uses a deliberately configured handler, and the Windows transport stops pre-authenticating requests.

**Tech Stack:** .NET 8, `SocketsHttpHandler`, `IHttpClientFactory`, xUnit, FluentAssertions, Microsoft.Extensions.DependencyInjection.

---

## Invariants

- No request may reserve more than `LocalMaxInFlight + LocalQueueCapacity` local slots; rejected work must not remain in workload counts or a wait queue.
- A local in-memory coordinator may never grant more than `MaximumRuntimeHosts` concurrent leases for one lease namespace. It remains `IsDurable == false`, so it is not a multi-host production coordinator.
- Both Dynamics HTTP paths must disable cookies, automatic redirects, proxies, automatic decompression, and pre-authentication. Authentication headers remain request-scoped.
- No product feature flag changes. `Package01FeeReadsEnabled` remains `false`.

### Task 1: Atomically bound local admission

**Files:**

- Modify: `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs`
- Modify: `SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs`

- [ ] **Step 1: Write the failing concurrency regression**

Add a test that acquires the only in-flight permit, releases a start barrier for many distinct workloads, and asserts that exactly `LocalQueueCapacity` requests can remain pending while every other request returns `QueueFull`. Cancel the pending requests, dispose every granted permit, and assert `Queued == 0`, `InFlight == 0`, and no per-workload reservation remains observable through a subsequent acquisition.

```csharp
var first = await manager.AcquireAsync(CreateEnvelope("holder"), CancellationToken.None);
var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var attempts = Enumerable.Range(0, 32).Select(async i =>
{
    await gate.Task;
    return await manager.AcquireAsync(CreateEnvelope($"burst-{i}"), cts.Token);
});
gate.SetResult();
```

- [ ] **Step 2: Run the test and record the red result**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests --filter "FullyQualifiedName~OrganizationAdmissionManagerTests.Concurrent_burst"
```

Expected before the fix: a burst can exceed the configured queue capacity or the new assertion fails to prove a strict bound.

- [ ] **Step 3: Reserve total capacity before waiting**

Add a second semaphore sized to `LocalMaxInFlight + LocalQueueCapacity`. While holding `_gate`, reserve a total slot with a non-blocking wait. Then attempt a non-blocking `_inFlight` wait:

```csharp
if (!_admissionSlots.Wait(0))
    return QueueFull();

if (_inFlight.Wait(0))
    return CreateAcceptedPermit(workload);

_queued++;
```

Queued work awaits only `_inFlight`; cancellation/timeout must release the total slot, queue count, and workload reservation exactly once. Permit disposal must release both `_inFlight` and `_admissionSlots` exactly once. Dispose both semaphores after lifetime cancellation.

- [ ] **Step 4: Run the focused test and the Dynamics test project**

```powershell
dotnet test SpeechMessage.Dynamics.Tests --filter "FullyQualifiedName~OrganizationAdmissionManagerTests"
dotnet test SpeechMessage.Dynamics.Tests --no-restore
```

Expected: all admission tests pass with no queued or permit leak after cancellation/disposal.

### Task 2: Make the in-memory host limit locally atomic

**Files:**

- Modify: `SpeechMessage.Dynamics.WebApi/Capacity/InMemoryRuntimeHostSlotCoordinator.cs`
- Modify: `SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs`

- [ ] **Step 1: Write the failing concurrent lease test**

Create many simultaneous `TryAcquireAsync` calls with distinct host IDs and a single-host limit. Assert that exactly one lease is returned, dispose it, then assert a later host can acquire the released slot.

```csharp
var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var attempts = Enumerable.Range(0, 64).Select(async i =>
{
    await start.Task;
    return await coordinator.TryAcquireAsync(ns, $"host-{i}", 1, ttl, CancellationToken.None);
});
start.SetResult();
var leases = await Task.WhenAll(attempts);
leases.Count(lease => lease is not null).Should().Be(1);
```

- [ ] **Step 2: Run the new test to verify red**

```powershell
dotnet test SpeechMessage.Dynamics.Tests --filter "FullyQualifiedName~OrganizationAdmissionManagerTests.Concurrent_host"
```

Expected before the fix: the count-then-set race can grant more than one slot under concurrent execution.

- [ ] **Step 3: Serialize the coordinator's local state transition**

Add a private lock that covers purge, existing-lease renewal, active-count, slot insertion, explicit renew, and fenced release. Do not change `IsDurable`; do not claim that a process-local lock coordinates replicas.

- [ ] **Step 4: Re-run focused and complete Dynamics tests**

```powershell
dotnet test SpeechMessage.Dynamics.Tests --filter "FullyQualifiedName~OrganizationAdmissionManagerTests"
dotnet test SpeechMessage.Dynamics.Tests --no-restore
```

Expected: exactly one local lease per namespace limit under the test burst.

### Task 3: Enforce no-session HTTP handler settings

**Files:**

- Modify: `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
- Modify: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs`
- Modify or create: `SpeechMessage.Dynamics.Tests/WebApiServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write a failing DI handler-policy test**

Register an `IHttpMessageHandlerBuilderFilter` that captures the primary handler for the `dynamics-adfs-token` client. Build the service provider with a valid ADFS options shape, create that client, and assert a `SocketsHttpHandler` whose cookie, redirect, proxy, decompression, and pre-authentication settings are all disabled.

- [ ] **Step 2: Run the test to verify red**

```powershell
dotnet test SpeechMessage.Dynamics.Tests --filter "FullyQualifiedName~WebApiServiceCollectionExtensionsTests.Adfs_token_client"
```

Expected before the fix: the primary handler is not explicitly hardened.

- [ ] **Step 3: Configure both HTTP paths explicitly**

Configure the named ADFS client with a new `SocketsHttpHandler`:

```csharp
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    UseCookies = false,
    AllowAutoRedirect = false,
    UseProxy = false,
    AutomaticDecompression = DecompressionMethods.None,
    PreAuthenticate = false,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
})
```

Set the main `DynamicsHttpTransport` handler's `PreAuthenticate` to `false`. Do not add authorization values to `DefaultRequestHeaders`.

- [ ] **Step 4: Run the handler and complete Dynamics tests**

```powershell
dotnet test SpeechMessage.Dynamics.Tests --filter "FullyQualifiedName~WebApiServiceCollectionExtensionsTests|FullyQualifiedName~DynamicsHttpTransport"
dotnet test SpeechMessage.Dynamics.Tests --no-restore
```

Expected: tests establish the same non-session handler policy for ADFS and Web API traffic.

### Task 4: Review and evidence update

**Files:**

- Modify: `.trellis/tasks/07-23-dynamics-connection-compatibility/task.json`
- Create: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-isolation-hardening-verification.md`

- [ ] **Step 1: Run the required local review and full focused verification**

Inspect `git diff --check`, run the Dynamics test project and relevant release builds, and perform the required Gemini+Claude review only after the owner has approved sending de-identified source/finding context externally.

- [ ] **Step 2: Record precise scope and remaining blockers**

Write the actual commands and results. State explicitly that durable coordination, profile generation/drain, bounded response streaming, production Gateway workload authentication, and live CRM/ADFS proof remain open unless fresh evidence proves otherwise.

- [ ] **Step 3: Preserve rollout safety**

Verify `Package01FeeReadsEnabled` stays `false`; do not run a consumer traffic migration or remove legacy SDK dependencies in this change set.

