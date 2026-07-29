# Dynamics Multi-Profile Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Local/Central Gateway shared runtime that routes `crm82` and `crm91` by server-owned alias, owns one isolated client/token/handler generation per profile, shares only canonical Organization admission capacity, and replaces generations with bounded drain and deterministic disposal.

**Architecture:** Keep the existing single-profile `AddSpeechMessageDynamicsWebApi` and `ControlledOperationExecutor` as compatibility APIs. Add a multi-profile manager above immutable profile generations. Each generation owns its `DynamicsHttpTransport`, `DynamicsWebApiClient`, `AdfsOAuthTokenProvider`, cancellation state, and an admission-registry registration; generations may share an `IOrganizationAdmissionManager` only when `CanonicalOrganizationCapacityKey = (ExpectedOrganizationId, NormalizedOrganizationBaseUri)` and the organization-owned capacity digest matches. Profile-local transport values such as `MaxConnectionsPerServer` are validated against that shared capacity but never participate in its digest. Replacement follows validated construction → host-slot acquisition → optional warm-up → atomic publication → active-plus-one-draining → bounded cancellation/drain → deterministic disposal.

**Tech Stack:** .NET 10, ASP.NET Core, Microsoft.Extensions.DependencyInjection/Options/Logging, xUnit, FluentAssertions, existing Dynamics Web API and Organization Admission components.

---

## Scope and invariants

- Central Gateway and Local Gateway remain deployments of `ExecutionMode=Gateway`; only their endpoint differs.
- Embedded remains compiled and unchanged by this milestone.
- Product requests contain alias, operation ID, typed parameters, idempotency key, and trusted workload identity only. They cannot supply endpoint, CE version, credentials, token, SDK kind, or transport.
- `crm82` and `crm91` never share mutable `HttpClient`, handler, token provider/cache, credential state, cancellation source, runtime generation, or client instance.
- Two profiles share an admission manager only when both canonical organization key and capacity configuration digest match exactly.
- Canonical collision checks are bidirectional: the same organization GUID cannot map to two base URIs, the same normalized base URI cannot map to two GUIDs, and one admission/lease namespace cannot map to two canonical organizations.
- One alias has at most one active generation and one draining generation.
- Publication and execution acquisition use the same alias-slot lock, so the retired generation cannot receive a new execution lease after publication.
- Admission is acquired before queue wait, but the active runtime execution lease is acquired only after that wait. Queued-undispatched work therefore uses the current active generation after a swap and never pins the old generation.
- If the alias resolves to a different canonical organization while a request waits, or runtime acquisition fails after admission, the admission permit is released and the request fails before outbound traffic.
- A third replacement while one replacement is constructing, warming, or draining fails closed; it does not allocate another generation.
- Every timer, `CancellationTokenSource`, `SemaphoreSlim`, HTTP handler/client, admission registration, execution lease, and background task has one bounded owner and an idempotent cleanup path.
- Phase 4 remains the verification phase, Phase 5 remains strangler migration, and Phase 6 remains final SDK/Data8 removal.

## File responsibility map

### Capacity ownership

- Modify `SpeechMessage.Dynamics.WebApi/Capacity/CapacityKeys.cs`: canonical organization key construction and reverse-collision-safe identity handling.
- Modify `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionPlan.cs`: remove profile-local `MaxConnectionsPerServer` from the shared configuration digest while retaining validation that it does not exceed `LocalMaxInFlight`.
- Create `SpeechMessage.Dynamics.WebApi/Capacity/IOrganizationAdmissionRegistry.cs`: registration and registry contracts.
- Create `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionRegistry.cs`: canonical-key lookup, GUID/base-URI/namespace reverse collision rejection, reference counting, and deterministic manager disposal.
- Preserve `OrganizationAdmissionManager.cs` and existing lease semantics; reuse rather than rewrite them.

### Profile generation ownership

- Create `SpeechMessage.Dynamics.WebApi/Runtime/ProfileRuntimeKey.cs`: non-secret alias/generation/canonical identity.
- Create `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileDefinition.cs`: validates alias and deep-copies mutable options.
- Create `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntime.cs`: runtime, execution-lease, state, and diagnostics contracts.
- Create `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs`: active execution accounting, drain cancellation, and ordered disposal.
- Create `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntimeFactory.cs`: generation construction contract.
- Create `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeFactory.cs`: creates isolated options/token/transport/client/executor resources and acquires admission registration.
- Modify `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`: make provider disposal idempotent, dispose its single-flight semaphore, clear cached references, and give factory-created generations a profile-owned token HTTP handler/client.

### Routing and replacement

- Create `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntimeManager.cs`: initialize, execute, replace, snapshot, and lifecycle contract.
- Create `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs`: alias slots, atomic publish, active-plus-one-draining, and shutdown.
- Create `SpeechMessage.Dynamics.WebApi/Runtime/IProfileExecutionLeaseProvider.cs`: combined admission-plus-current-runtime lease contract used after queue wait.
- Create `SpeechMessage.Dynamics.WebApi/Runtime/ProfileRoutedOperationExecutor.cs`: `IDynamicsOperationExecutor` adapter over the manager.
- Modify `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`: retain the existing `(IDynamicsWebApiClient, IOrganizationAdmissionManager)` constructor while adding the combined lease-provider path.

### Host wiring

- Modify `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`: add a multi-profile registration method without breaking the single-profile method.
- Modify `SpeechMessage.Dynamics.Gateway/Program.cs`: load `DynamicsProfiles:Profiles:{alias}` with a legacy `DynamicsWebApi` fallback, register the multi-profile path, and expose aggregate readiness.
- Modify `SpeechMessage.Dynamics.Gateway/DynamicsGatewayReadinessService.cs`: verify coordinator schema, initialize all profiles, and dispose the manager on shutdown.
- Modify `SpeechMessage.Dynamics.Gateway/appsettings.json`: move the existing CE 8.2 definition under `DynamicsProfiles:Profiles:crm82`; do not invent an unverified production `crm91` endpoint or credential.

### Tests

- Create `SpeechMessage.Dynamics.Tests/OrganizationAdmissionRegistryTests.cs`.
- Create `SpeechMessage.Dynamics.Tests/DynamicsProfileRuntimeFactoryTests.cs`.
- Create `SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs`.
- Modify `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`.
- Modify `SpeechMessage.Dynamics.Tests/GatewayReadinessTests.cs`.
- Modify `SpeechMessage.Dynamics.Tests/Phase4IsolationSoakTests.cs` only for the final multi-profile replace/drain resource baseline; preserve existing tests.

---

### Task 1: Write the multi-profile RED contracts

**Files:**
- Create: `SpeechMessage.Dynamics.Tests/OrganizationAdmissionRegistryTests.cs`
- Create: `SpeechMessage.Dynamics.Tests/DynamicsProfileRuntimeFactoryTests.cs`
- Create: `SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs`
- Modify: `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`

- [ ] **Step 1: Add failing admission-registry tests**

Add tests with these exact behaviors:

```csharp
[Fact]
public void Same_canonical_organization_and_digest_share_one_manager()

[Fact]
public void Same_canonical_organization_with_different_capacity_digest_fails_closed()

[Fact]
public void Different_canonical_organizations_get_different_managers()

[Fact]
public void Same_organization_id_with_different_base_uri_fails_closed()

[Fact]
public void Same_base_uri_with_different_organization_id_fails_closed()

[Fact]
public void Same_admission_or_lease_namespace_cannot_bind_different_canonical_organizations()

[Fact]
public async Task Last_registration_disposes_manager_and_removes_registry_entry()

[Fact]
public async Task Registry_shutdown_is_idempotent_and_disposes_every_remaining_manager_once()
```

Use plans created by `OrganizationAdmissionPlan.TryCreate`. The same-organization test must use exactly the same expected organization GUID and normalized base URI. A valid different-organization test changes both typed tuple members; changing only one member is a partial collision and must fail closed. Do not derive equality from alias, version label, IP address, or FQDN alone. Add one test proving two profiles for the same canonical organization may use different `MaxConnectionsPerServer` values without changing the shared admission digest, while each value still must not exceed shared `LocalMaxInFlight`.

- [ ] **Step 2: Add failing runtime-factory ownership tests**

Add tests with these exact behaviors:

```csharp
[Fact]
public async Task Crm82_and_crm91_generations_own_distinct_clients_tokens_transports_and_handlers()

[Fact]
public async Task Disposing_generation_disposes_transport_token_provider_and_admission_registration_once()

[Fact]
public async Task Runtime_key_contains_no_user_session_jwt_token_or_credential_value()
```

Create runtimes with `WarmUpOnActivation=false` so construction does not contact a real server. Reflection may inspect private runtime resources, matching the existing handler-ownership test style, but assertions must compare object identity and disposal counts rather than secret values.

- [ ] **Step 3: Add failing manager routing and replacement tests**

Use a fake `IDynamicsProfileRuntimeFactory` and tracking fake runtimes so the manager is tested without network calls:

```csharp
[Fact]
public async Task Crm82_and_crm91_route_to_their_own_active_runtime()

[Fact]
public async Task Unknown_alias_fails_before_runtime_factory_or_transport()

[Fact]
public async Task Replacement_publishes_new_generation_atomically_and_old_generation_gets_no_new_leases()

[Fact]
public async Task Existing_old_generation_work_drains_before_disposal()

[Fact]
public async Task Rapid_third_replacement_is_rejected_without_allocating_a_generation()

[Fact]
public async Task Drain_timeout_cancels_bounded_old_generation_work()

[Fact]
public async Task Manager_shutdown_is_idempotent_and_releases_all_strong_references()

[Fact]
public async Task Queued_request_uses_the_new_active_generation_after_swap()

[Fact]
public async Task Runtime_acquisition_failure_releases_the_admission_permit()
```

The replacement test must hold one old-generation execution, publish generation 2, start a new execution, and prove the new execution reaches only generation 2 while the held execution finishes on generation 1. The queued-request test must acquire shared admission first, remain queued without holding a runtime reference, then use generation 2 after publication. If the canonical organization binding changes while queued, release the permit and reject before runtime/transport dispatch.

- [ ] **Step 4: Add failing token-provider lifecycle test**

Add:

```csharp
[Fact]
public async Task Disposed_provider_rejects_new_token_work_and_releases_owned_http_resources()
```

The test must prove repeated `Dispose`/`DisposeAsync` is harmless and `GetAccessTokenAsync` after disposal throws `ObjectDisposedException` before secret resolution or HTTP dispatch.

- [ ] **Step 5: Run RED tests**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore --filter "FullyQualifiedName~OrganizationAdmissionRegistryTests|FullyQualifiedName~DynamicsProfileRuntimeFactoryTests|FullyQualifiedName~MultiProfileRuntimeTests|FullyQualifiedName~AdfsOAuthTokenProviderTests"
```

Expected: FAIL because the new registry/runtime/manager contracts do not exist and the token provider is not disposable. Record the compile/test failure in the task notes before production implementation.

---

### Task 2: Implement reference-counted canonical Organization admission

**Files:**
- Modify: `SpeechMessage.Dynamics.WebApi/Capacity/CapacityKeys.cs`
- Modify: `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionPlan.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Capacity/IOrganizationAdmissionRegistry.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionRegistry.cs`
- Test: `SpeechMessage.Dynamics.Tests/OrganizationAdmissionRegistryTests.cs`

- [ ] **Step 1: Define the registration contract**

Use these signatures:

```csharp
public interface IOrganizationAdmissionRegistration : IAsyncDisposable, IDisposable
{
    OrganizationAdmissionPlan Plan { get; }
    IOrganizationAdmissionManager Manager { get; }
}

public interface IOrganizationAdmissionRegistry : IAsyncDisposable, IDisposable
{
    IOrganizationAdmissionRegistration Acquire(OrganizationAdmissionPlan plan);
    int EntryCount { get; }
}
```

- [ ] **Step 2: Implement canonical lookup and collision validation**

`OrganizationAdmissionRegistry` uses one private lock, a `Dictionary<CanonicalOrganizationCapacityKey, Entry>`, and reverse maps for expected organization GUID, normalized organization base URI, admission namespace, and runtime-host lease namespace. `Acquire` must:

1. Throw after registry disposal.
2. Find by `plan.CanonicalKey` only.
3. If an entry exists, require ordinal equality of `ConfigurationDigest`, `AdmissionKey`, `LeaseNamespace`, and `AdmissionEpoch`; otherwise throw `InvalidOperationException` before any new manager is created.
4. Increment an entry reference count and return a new idempotent registration.
5. Reject a partial canonical collision (same GUID/different URI or same URI/different GUID) before a new manager is created.
6. Reject an admission or lease namespace already bound to another canonical organization.
7. If absent, create `OrganizationAdmissionManager(plan, coordinator, logger)`, store it with reference count 1, populate every reverse map, and return the registration.

- [ ] **Step 3: Separate shared admission digest from profile-local transport validation**

`OrganizationAdmissionPlan.ConfigurationDigest` contains only canonical organization and `OrganizationAdmissions` capacity/lease fields. Remove `MaxConnectionsPerServer` from that digest. Keep strict profile validation requiring `1 <= MaxConnectionsPerServer <= LocalMaxInFlight`; transport differences therefore cannot split one physical organization's host-slot epoch or capacity budget.

- [ ] **Step 4: Implement release and registry shutdown**

Registration disposal decrements exactly once. When the count reaches zero, remove the exact entry and all of its reverse-map bindings under the registry lock, then await `entry.Manager.DisposeAsync()` outside the lock. Registry shutdown atomically marks itself disposed, clears entries and reverse maps, and disposes each remaining manager once with `Task.WhenAll`. Late registration disposal after registry shutdown is a no-op rather than a second manager disposal.

- [ ] **Step 5: Run registry tests GREEN**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore --filter FullyQualifiedName~OrganizationAdmissionRegistryTests
```

Expected: all registry tests pass with zero failures.

---

### Task 3: Implement isolated profile generation resources

**Files:**
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/ProfileRuntimeKey.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileDefinition.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntime.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntimeFactory.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeFactory.cs`
- Modify: `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
- Test: `SpeechMessage.Dynamics.Tests/DynamicsProfileRuntimeFactoryTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`

- [ ] **Step 1: Define immutable identity and definition types**

Use a non-secret key:

```csharp
public readonly record struct ProfileRuntimeKey(
    string ProfileAlias,
    long Generation,
    string CeVersion,
    CanonicalOrganizationCapacityKey CanonicalOrganizationKey);
```

`DynamicsProfileDefinition` validates aliases with the same strict rule as ProductClient: 1-128 characters, letters/digits/`.`/`_`/`-`, no whitespace, path separators, query, fragment, or URI syntax. It deep-copies every `DynamicsWebApiOptions` and nested `OrganizationAdmissionOptions` property. It exposes `CreateOptionsSnapshot()` so each factory call receives a new mutable copy that is never shared with another generation.

Definition properties:

```csharp
public string ProfileAlias { get; }
public bool WarmUpOnActivation { get; }
public TimeSpan DrainTimeout { get; }
public TimeSpan CancellationGracePeriod { get; }
public DynamicsWebApiOptions CreateOptionsSnapshot();
```

Default drain timeout comes from `ShutdownDrainTimeoutSeconds`; cancellation grace is `MaximumOutboundWorkLifetimeSeconds + RuntimeHostSlotExpiryFenceSeconds + 5 seconds`.

- [ ] **Step 2: Define runtime and execution-lease contracts**

```csharp
public enum DynamicsProfileRuntimeState { Active, Draining, Disposed }

public interface IDynamicsProfileExecutionLease : IAsyncDisposable, IDisposable
{
    ProfileRuntimeKey RuntimeKey { get; }
    Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken);
}

public interface IDynamicsProfileRuntime : IAsyncDisposable, IDisposable
{
    ProfileRuntimeKey Key { get; }
    DynamicsProfileRuntimeState State { get; }
    int ActiveExecutionCount { get; }
    AdmissionMetricsSnapshot AdmissionSnapshot { get; }
    bool TryAcquireExecution(out IDynamicsProfileExecutionLease? lease);
    Task<OperationExecutionResult> WarmUpAsync(CancellationToken cancellationToken);
    void BeginDrain();
    Task DrainAndDisposeAsync(CancellationToken cancellationToken = default);
}

public interface IDynamicsProfileRuntimeFactory
{
    Task<IDynamicsProfileRuntime> CreateAsync(
        DynamicsProfileDefinition definition,
        long generation,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Make ADFS token ownership deterministic**

Change `AdfsOAuthTokenProvider` to implement `IDisposable` and `IAsyncDisposable`. When constructed without `IHttpClientFactory`, create one provider-owned `SocketsHttpHandler`/`HttpClient` with cookies, redirect, proxy, decompression, and pre-auth disabled; reuse it for token refreshes and dispose it with the provider. When constructed with `IHttpClientFactory`, preserve current wrapper-per-request disposal behavior. `Dispose` must be idempotent, set cached token references to `null`, reset expiry, dispose the semaphore, and make later calls fail with `ObjectDisposedException` before any I/O.

- [ ] **Step 4: Implement the runtime execution lease**

`DynamicsProfileRuntime.TryAcquireExecution` locks its lifecycle gate, accepts only `Active`, increments the active count, and returns an idempotent lease. The lease creates a linked token from caller cancellation and the runtime retirement token, executes the existing single-profile `ControlledOperationExecutor`, then decrements the count in `Dispose`. The transition to zero completes a `TaskCompletionSource` created when the first active execution begins.

- [ ] **Step 5: Implement bounded drain and ordered disposal**

`BeginDrain` changes `Active` to `Draining` once. `DrainAndDisposeAsync`:

1. Calls `BeginDrain`.
2. Waits up to `DrainTimeout` for active execution count zero.
3. On timeout, cancels the retirement token.
4. Waits up to `CancellationGracePeriod` for all leases to release.
5. Throws `TimeoutException` without disposing in-use transport resources if leases still remain.
6. After zero, disposes transport, token provider, admission registration, retirement source, and internal synchronization primitives exactly once.

- [ ] **Step 6: Implement factory construction and rollback**

`DynamicsProfileRuntimeFactory.CreateAsync` must:

1. Deep-copy options from the definition.
2. validate `ApprovedWebApiRootFactory` and `OrganizationAdmissionPlan.TryCreate`;
3. acquire an admission registration;
4. create a new `DynamicsHttpTransport` from the generation options;
5. create a new `AdfsOAuthTokenProvider` without the global `IHttpClientFactory`, so the token handler/client belongs to this generation;
6. create `DynamicsWebApiClient` and the existing `ControlledOperationExecutor`;
7. return `DynamicsProfileRuntime` with a non-secret `ProfileRuntimeKey`;
8. if any construction step fails, dispose every previously created resource in reverse ownership order before rethrowing.

- [ ] **Step 7: Run runtime ownership tests GREEN**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore --filter "FullyQualifiedName~DynamicsProfileRuntimeFactoryTests|FullyQualifiedName~AdfsOAuthTokenProviderTests"
```

Expected: all tests pass; no generation shares the main or token HTTP client/handler, token provider, or admission registration object.

---

### Task 4: Implement alias routing and active-plus-one-draining replacement

**Files:**
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntimeManager.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/IProfileExecutionLeaseProvider.cs`
- Create: `SpeechMessage.Dynamics.WebApi/Runtime/ProfileRoutedOperationExecutor.cs`
- Modify: `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
- Test: `SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs`

- [ ] **Step 1: Define manager snapshot and contract**

```csharp
public sealed record DynamicsProfileSnapshot(
    ProfileRuntimeKey Key,
    DynamicsProfileRuntimeState State,
    int ActiveExecutionCount,
    AdmissionMetricsSnapshot Admission);

public sealed record DynamicsProfileRuntimeManagerSnapshot(
    bool IsReady,
    int ActiveProfileCount,
    int DrainingProfileCount,
    IReadOnlyList<DynamicsProfileSnapshot> Profiles);

public interface IDynamicsProfileRuntimeManager : IAsyncDisposable, IDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default);
    Task ReplaceAsync(
        DynamicsProfileDefinition definition,
        CancellationToken cancellationToken = default);
    DynamicsProfileRuntimeManagerSnapshot GetSnapshot();
}
```

The manager also implements a combined execution-lease provider. Its successful lease owns the `IAdmissionPermit`, current active runtime execution lease, `IDynamicsWebApiClient`, non-secret `ProfileRuntimeKey`, and the linked host/generation fence token. Disposal order is runtime execution lease first, then admission permit. The provider releases the permit on every failure path after admission.

- [ ] **Step 2: Implement one alias slot and generation sequencing**

Each alias slot owns:

```csharp
object SyncRoot;
SemaphoreSlim ReplacementGate;
IDynamicsProfileRuntime Active;
IDynamicsProfileRuntime? Draining;
long LastGeneration;
```

Manager construction validates a non-empty, duplicate-free initial definition set using `StringComparer.OrdinalIgnoreCase` and stores only definitions, not live runtime resources.

- [ ] **Step 3: Implement initialization with rollback**

`InitializeAsync` is single-flight and idempotent. Construct each initial runtime, call `AdmissionSnapshot` only after its admission manager has acquired a host slot, optionally call `WarmUpAsync`, then publish. If any profile fails, drain/dispose every runtime built during that initialization attempt and leave the manager NotReady.

- [ ] **Step 4: Implement fail-closed routing and queue-to-runtime acquisition order**

`ExecuteAsync` validates request and alias. Unknown alias returns `DynamicsErrorCodes.NotReady` before invoking secret resolution, admission, factory, runtime, token, or transport. For a known alias, resolve the immutable alias-to-canonical binding, acquire the shared admission permit and wait for capacity without holding a runtime reference, then lock `slot.SyncRoot` and acquire the current `slot.Active` execution lease. Recheck that the canonical binding is unchanged before dispatch. Publication uses the same lock, proving no lease can be acquired from the old runtime after the new runtime is published. Any runtime-acquisition or binding-recheck failure disposes the admission permit before returning.

- [ ] **Step 5: Implement replacement**

`ReplaceAsync` uses `ReplacementGate.WaitAsync(TimeSpan.Zero)`; failure throws `InvalidOperationException("A replacement is already active for this profile.")` before factory allocation. On success:

1. create and validate generation `LastGeneration + 1`;
2. ensure host slot and optionally warm it;
3. under `slot.SyncRoot`, verify `Draining is null`, call old `BeginDrain`, assign new `Active`, assign old `Draining`, and increment generation;
4. outside the lock, await old `DrainAndDisposeAsync`;
5. under the lock, clear the exact old draining reference;
6. on pre-publication failure, dispose the new runtime and keep old active;
7. always release `ReplacementGate`.

- [ ] **Step 6: Implement shutdown**

Shutdown atomically rejects new routing, marks all active runtimes draining under their slot locks, drains/disposes active and already-draining runtimes, disposes each replacement semaphore, clears the alias dictionary, and is idempotent through one cached shutdown task.

- [ ] **Step 7: Add the product-facing adapter**

`ProfileRoutedOperationExecutor` contains only an `IDynamicsProfileRuntimeManager` and delegates `ExecuteAsync`. It must not cache alias, request identity, token, or client state.

Keep `ControlledOperationExecutor(IDynamicsWebApiClient, IOrganizationAdmissionManager)` as the single-profile compatibility constructor. Add a combined lease-provider construction path so multi-profile execution does not acquire admission twice and can dispose the runtime execution lease before the shared admission permit.

- [ ] **Step 8: Run manager tests GREEN**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore --filter FullyQualifiedName~MultiProfileRuntimeTests
```

Expected: routing, unknown-alias rejection, atomic publication, old-work drain, third-replacement rejection, timeout cancellation, and idempotent shutdown all pass.

---

### Task 5: Wire Gateway configuration, readiness, and compatibility

**Files:**
- Modify: `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
- Modify: `SpeechMessage.Dynamics.Gateway/Program.cs`
- Modify: `SpeechMessage.Dynamics.Gateway/DynamicsGatewayReadinessService.cs`
- Modify: `SpeechMessage.Dynamics.Gateway/appsettings.json`
- Modify: `SpeechMessage.Dynamics.Tests/GatewayReadinessTests.cs`

- [ ] **Step 1: Add multi-profile DI without breaking single-profile DI**

Add:

```csharp
public static IServiceCollection AddSpeechMessageDynamicsProfiles(
    this IServiceCollection services,
    IReadOnlyCollection<DynamicsProfileDefinition> profiles)
```

Register `ISecretResolver`, default `IRuntimeHostSlotCoordinator`, `IOrganizationAdmissionRegistry`, `IDynamicsProfileRuntimeFactory`, `IDynamicsProfileRuntimeManager`, and `IDynamicsOperationExecutor` as singletons. Do not register a global `IDynamicsWebApiClient`, `IDynamicsHttpTransport`, or `IAdfsOAuthTokenProvider` in this overload. Keep `AddSpeechMessageDynamicsWebApi` behavior intact for Embedded and smoke-test compatibility.

- [ ] **Step 2: Load deployment-owned profiles in Gateway**

`Program.cs` reads child sections under `DynamicsProfiles:Profiles`. Each child name is the alias and each child binds one `DynamicsWebApiOptions`; `WarmUpOnActivation` is read from the same child. If no children exist, bind the legacy `DynamicsWebApi` section and derive alias `crm82` for CE 8.2 or `crm91` for CE 9.1. Missing endpoint fallback remains localhost only for the Testing environment; non-Testing startup fails closed instead of inventing a production route.

- [ ] **Step 3: Migrate current Gateway appsettings**

Move the current CE 8.2 settings to:

```json
"DynamicsProfiles": {
  "Profiles": {
    "crm82": {
      "WarmUpOnActivation": false,
      "OrganizationBaseUri": "https://jesus.speechmessage.com.tw/",
      "OrganizationWebApiBaseUri": "https://jesus.speechmessage.com.tw/api/data/v8.2/",
      "CeVersion": "8.2"
    }
  }
}
```

Preserve all existing secret references and admission settings under `crm82`. Do not add plaintext secrets. Do not add a fake production `crm91` profile.

- [ ] **Step 4: Initialize profiles from readiness service**

`DynamicsGatewayReadinessService.StartAsync` verifies SQL coordinator schema, then calls `IDynamicsProfileRuntimeManager.InitializeAsync`. `StopAsync` calls manager `DisposeAsync`. The manager handles host-slot acquisition per unique canonical organization; shared profiles reuse the same manager and lease.

- [ ] **Step 5: Make `/ready` aggregate profile readiness**

Return 200 only when manager snapshot is ready and every active profile has safe admission readiness. Response contains alias, generation, state, active executions, in-flight, queued, active permits, lease expiry, and renewal-loop state only. It must not contain endpoint, secret reference, credential, token, raw exception, or request identity. Preserve `Cache-Control: no-store`.

- [ ] **Step 6: Update readiness tests**

Replace the single admission stub with an `IDynamicsProfileRuntimeManager` stub. Verify all-ready returns 200, any-profile-not-ready returns 503, output is no-store, and the serialized body does not expose endpoint/secret/token fields.

- [ ] **Step 7: Run host/DI tests**

Run:

```powershell
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore --filter "FullyQualifiedName~WebApiServiceCollectionExtensionsTests|FullyQualifiedName~GatewayReadinessTests|FullyQualifiedName~GatewayWorkloadBoundaryTests"
```

Expected: all pass and legacy single-profile registration remains available.

---

### Task 6: Run lifecycle, isolation, and full regression gates

**Files:**
- Modify: `SpeechMessage.Dynamics.Tests/Phase4IsolationSoakTests.cs`
- Create: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-multi-profile-runtime-verification.md`

- [ ] **Step 1: Add repeated replacement resource-baseline test**

Add one test that runs at least 16 `crm82`/`crm91` replacement cycles with five workloads, holds work across selected publications, then drains and shuts down. Track weak references for manager, runtime, transport, token provider, handler, cancellation sources where observable, and fake factory resources. After forced full GC, require retired objects to be unreachable and managed memory/handle/thread counts to remain within the existing bounded post-warm-up tolerances.

- [ ] **Step 2: Add canonical shared-admission concurrency test**

Run two aliases with different clients/generations but the same canonical organization plan. Prove their combined observed outbound work never exceeds the one shared `LocalMaxInFlight`. Run another alias with a different canonical organization and prove it receives a separate budget.

- [ ] **Step 3: Run focused Phase 4 tests**

```powershell
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore --filter "FullyQualifiedName~MultiProfileRuntimeTests|FullyQualifiedName~OrganizationAdmissionRegistryTests|FullyQualifiedName~DynamicsProfileRuntimeFactoryTests|FullyQualifiedName~Phase4IsolationSoakTests"
```

Expected: zero failures and all resource counters return to zero/baseline.

- [ ] **Step 4: Run the complete Dynamics suite and solution build**

```powershell
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore
dotnet build SpeechMessageProducts.sln --configuration Release --no-restore
dotnet list PowerPlatform.Dataverse.Client package --vulnerable --include-transitive
git diff --check
```

Expected: all tests pass, build has zero errors, no vulnerable package is reported, and diff check is clean.

- [ ] **Step 5: Run source-level isolation audit**

Search new runtime files for static/shared mutable session, token, credential, client, handler, `AsyncLocal`, and `ThreadLocal` state. Search added lines for credential values, connection strings, access tokens, passwords, and private keys. Any finding is a release blocker unless it is a type/property name in a documented allowlist.

- [ ] **Step 6: Record evidence**

Write exact RED/GREEN commands, test counts, build output, object/resource baseline results, known limitations, and rollback shape to `phase4-multi-profile-runtime-verification.md`.

---

## Parallel execution ownership

The CCG L+ workflow uses `fork_turns="none"` and non-overlapping files.

### Layer 1 — RED tests

- Test worker owns only:
  - `SpeechMessage.Dynamics.Tests/OrganizationAdmissionRegistryTests.cs`
  - `SpeechMessage.Dynamics.Tests/DynamicsProfileRuntimeFactoryTests.cs`
  - `SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs`
  - `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`

### Layer 2 — independent foundations

- Capacity worker owns only:
  - `SpeechMessage.Dynamics.WebApi/Capacity/CapacityKeys.cs`
  - `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionPlan.cs`
  - `SpeechMessage.Dynamics.WebApi/Capacity/IOrganizationAdmissionRegistry.cs`
  - `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionRegistry.cs`
- Generation worker owns only:
  - `SpeechMessage.Dynamics.WebApi/Runtime/ProfileRuntimeKey.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileDefinition.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntime.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntimeFactory.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeFactory.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`

### Layer 3 — routing manager

- Manager worker owns only:
  - `SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsProfileRuntimeManager.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/IProfileExecutionLeaseProvider.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/ProfileRoutedOperationExecutor.cs`
  - `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`

### Layer 4 — Gateway host

- Host worker owns only:
  - `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
  - `SpeechMessage.Dynamics.Gateway/Program.cs`
  - `SpeechMessage.Dynamics.Gateway/DynamicsGatewayReadinessService.cs`
  - `SpeechMessage.Dynamics.Gateway/appsettings.json`
  - `SpeechMessage.Dynamics.Tests/GatewayReadinessTests.cs`

### Layer 5 — verification

- Review worker owns only fixes required by review after explicit reassignment.
- Lead agent runs the complete tests/build/audits and writes the verification report.
- External Gemini + Claude review runs through `docs/scripts/Start-CcgDualModelRun.ps1`; quota fallback must be reported as degraded, never as full dual-model success.

## Rollback points

- The existing `AddSpeechMessageDynamicsWebApi` single-profile path remains available; reverting Gateway registration restores the previous host path without reintroducing product-side SDK dependencies.
- A failed replacement never publishes the new generation; the last validated active generation remains in service.
- A published replacement that cannot drain cancels bounded old work but does not dispose transport resources while execution leases remain.
- Admission configuration collision fails startup/replacement before new outbound Dynamics traffic.
- No change in this milestone removes Data8 or Embedded; those remain governed by their later Phase 4–6 gates.
