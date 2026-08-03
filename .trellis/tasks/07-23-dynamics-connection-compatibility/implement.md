# Implementation plan: official Microsoft NuGet Dynamics Gateway

## 2026-08-02 authoritative direction

This plan supersedes the earlier direct-Web-API-first and universal-no-SDK
execution plan.

The only supported Dynamics transport in this task is:

```text
Product (.NET 10)
  -> Central or Local Gateway (.NET 10)
  -> bounded version-specific worker process (.NET Framework 4.8)
  -> Microsoft.CrmSdk.XrmTooling.CoreAssembly / CrmServiceClient
  -> CE 8.2 or CE 9.1 Organization Service
```

Direct Web API is not a primary route, fallback, future adapter, readiness gate,
or Phase 4 prerequisite. The D365APP01 CRMWeb/IFD HTTP 500, Deployment
PowerShell access, ASP.NET 1309 evidence, IFD wizard, and direct Web API
`WhoAmI` investigation are closed as Gateway blockers. Existing WebApi code and
smoke artifacts are legacy replacement inputs pending deletion after the
official worker path replaces their remaining dependencies.

No product traffic is enabled while the official-worker gates remain open:
`Package01FeeReadsEnabled=false`. Phase 5 migration and Phase 6 legacy removal
remain locked until the relevant Phase 4 gates pass.

## 2026-08-03 Phase 4C compatibility-harness increment

`docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.ps1` now provides the
first deployable compatibility evidence entrypoint for the official NuGet worker
route. Its `ValidateOnly` and `EnableLiveCompatibility` modes both reject an
invalid Gateway target before manifest/overlay evidence can be recorded. The
script validates only the pinned manifest, selected overlay profile,
worker-profile XML, package lock, worker kind, CE version, organization identity
and executable SHA-256 chain; it never uses direct Web API, D365APP01 management
PowerShell, an IFD wizard, or a transport fallback.

`ValidateOnly` creates no network resource. `EnableLiveCompatibility` can make
exactly one Gateway request for the fixed `runtime.health.whoami` operation only
after the same fail-closed preflight passes. The handler disables cookies,
proxy, redirects and decompression, uses the current Windows identity, bounds
connection/response/time limits, and deterministically disposes every request,
response, stream, buffer, cancellation source, client and handler. It emits
sanitized evidence only; Gateway URI, CRM endpoint, organization GUID,
credential reference, token, cookie and CRM body are excluded.

The matching regression test has passed. The remaining Phase 4C work is not a
script failure: an operator must still supply authoritative non-production or
approved CE 8.2/9.1 profile identity inputs and stable paths for one Local
Gateway generation, then run the complete website -> localhost Gateway ->
official worker operation matrix from Visual Studio. A separate Central/IIS
deployment is not required for this compatibility lane.

## Preserved completed work

Do not restart the Gateway foundation from zero. Preserve the existing:

- product-neutral operation contracts and ProductClient boundary;
- Central/Local Gateway endpoint model;
- bounded request validation, authorization, redaction, audit, and no-store
  behavior;
- immutable profile generations, replace-and-drain, cancellation, and
  deterministic disposal work;
- organization admission, durable epoch/slot fencing, LocalDB cross-process
  capacity, crash/quarantine, and fail-closed coordinator evidence;
- SQL coordinator test worker and current cross-process tests;
- `Package01FeeReadsEnabled=false` rollout boundary.

The new worker supervisor must integrate with these foundations rather than
forking a second capacity, identity, audit, or lifecycle implementation.

## Non-negotiable boundaries

1. Only `SpeechMessage.Dynamics.Crm82Worker` and
   `SpeechMessage.Dynamics.Crm91Worker`, plus worker-only tests, may reference
   Microsoft CRM SDK/XRM tooling packages or types.
2. Products, ProductClient, Abstractions, Gateway, Embedded, and ordinary tests
   remain free of Microsoft.Xrm, Microsoft.CrmSdk,
   Microsoft.PowerPlatform.Dataverse.Client, `IOrganizationService`, and SDK
   assemblies.
3. CE 8.2 and CE 9.1 workers are different executables with independent package
   locks. No binding redirect or shared AppDomain may combine their SDK graphs.
4. IPC carries only protocol metadata, an operation ID, immutable operation
   revision, deadline, nonce, bounded typed parameters, and a bounded typed
   result/error. It never carries SDK objects, raw FetchXML, CRM URLs,
   connection strings, credentials, tokens, cookies, principals, browser
   sessions, LINE IDs, or `HttpContext`.
5. Secrets do not appear in command-line arguments, environment variables,
   logs, exception text, crash evidence, product JSON, persisted IPC, or test
   fixtures. The preferred design resolves endpoint and credential references
   inside the worker from an approved local secret provider.
6. Every process, pipe, stream, client, timer, cancellation registration,
   semaphore, background task, and retained collection has one bounded owner
   and a deterministic cleanup path.
7. A failed official worker request never falls back to Web API, Data8, another
   CE version, another profile, or another credential.
8. Performance means maximum safe sustained throughput. Isolation, bounded
   admission, cleanup, and correctness cannot be weakened for benchmark speed.

## Phase 0 - Re-baseline the selected packages and boundaries

1. Record the current package facts:
   - the checked-in `PowerPlatform.Dataverse.Client` project is Data8 source,
     targets .NET 10, and is not Microsoft-owned transport source;
   - ChurchReport currently references the official
     `Microsoft.PowerPlatform.Dataverse.Client`, but product-side SDK references
     remain migration debt and are not the new architecture;
   - the local NuGet cache contains
     `Microsoft.CrmSdk.XrmTooling.CoreAssembly` 9.1.1.65 for the CE 9.1 worker;
   - the CE 8.2 worker package version must be selected from a Microsoft-published
     XRM tooling line and proven against the actual CE 8.2 target. Do not assume
     the CE 9.1 package is compatible merely because it restores or compiles.
2. Create immutable package-lock manifests for both workers. Each manifest pins
   package ID, version, package hash, target framework, executable hash, protocol
   version, and supported CE profile kind.
3. Change routing/configuration tests so the supported transport set contains
   only `OfficialCrm82Worker` and `OfficialCrm91Worker`.
4. Mark all direct-Web-API smoke/configuration paths as legacy pending removal.
   No readiness or feature flag may depend on them.
5. Update the source-boundary scan so SDK packages are allowed only in the two
   worker projects and explicit worker-only tests. Every other SDK hit fails.

## Phase 1 - TDD worker protocol and project boundaries

Write the failing tests before worker implementation.

1. Add `SpeechMessage.Dynamics.WorkerProtocol`, targeting `netstandard2.0`, with
   no CRM SDK package. It owns:
   - length-prefixed frame encoding;
   - protocol version and maximum frame size;
   - one-time process nonce and request ID;
   - profile-generation ID and immutable operation revision;
   - absolute deadline and bounded typed parameter/result envelopes;
   - sanitized error categories.
2. Add project-boundary tests that prove:
   - only the explicit worker allowlist references Microsoft CRM SDK packages;
   - products/Gateway cannot reference workers or `WorkerProtocol` directly;
   - worker projects do not reference each other;
   - no SDK type appears in a public protocol member or serialized payload;
   - `SpeechMessage.Dynamics.WebApi` is not selectable by any profile/runtime
     factory.
3. Add protocol tests for malformed length, oversized frame, wrong version,
   wrong nonce, duplicate request ID, expired deadline, unknown operation,
   excessive nesting/member count/string length, trailing data, and partial
   stream reads.
4. Add secret-leak regression tests that scan args, environment projections,
   logs, errors, protocol captures, and test snapshots for connection-string,
   password, token, or credential material.
5. Add lifecycle test doubles with counters for process, pipe, stream,
   cancellation source/registration, request permit, and queued-frame ownership.
   Every completed, cancelled, timed-out, crashed, or drained case must return to
   zero.

## Phase 2 - Implement version-specific official workers

1. Create `SpeechMessage.Dynamics.Crm91Worker`:
   - target .NET Framework 4.8;
   - pin Microsoft-published
     `Microsoft.CrmSdk.XrmTooling.CoreAssembly` 9.1.1.65 unless restore/package
     verification shows a newer explicitly approved lock;
   - construct one worker-owned `CrmServiceClient` from worker-local resolved
     configuration;
   - validate `IsReady`, sanitized last-error state, and an official-client
     identity operation before reporting Ready;
   - dispose the client exactly once during graceful shutdown.
2. Create `SpeechMessage.Dynamics.Crm82Worker`:
   - target .NET Framework 4.8;
   - pin a separately verified Microsoft XRM tooling version compatible with
     the actual CE 8.2 target;
   - use the same SDK-free protocol but an independent executable/package graph;
   - never load Data8 or CE 9.1 worker assemblies.
3. Each worker implements only registered typed operations. Translation to
   `Entity`, `QueryExpression`, server-owned FetchXML, or Organization requests
   happens entirely inside the worker. Generic `Execute`, caller-supplied
   FetchXML, caller-supplied entity/table names, or caller-supplied endpoint is
   prohibited.
4. Each worker uses finite bounds for request bytes, response bytes, entity
   count, page count, attribute count, string/blob size, batch size, timeout,
   queue depth, and cache size.
5. Default `MaxInFlightPerWorker=1` until exact package/target stress evidence
   proves safe concurrent use. Throughput initially scales through a bounded
   process pool under the existing organization admission budget.
6. On graceful drain, stop accepting frames, finish or cancel admitted work
   within the deadline, dispose the official client, close the pipe, and exit.
   If an SDK call cannot stop within the grace deadline, the supervisor
   terminates the process; OS process exit is the final WCF/handle/memory cleanup
   boundary.
7. Workers emit only allowlisted structured telemetry: worker kind, package-lock
   ID, operation ID/revision, duration, retry count, sanitized outcome, process
   generation, recycle reason, and resource counters. Never emit CRM URL,
   connection string, credential, token, raw query, entity payload, or exception
   object serialization.

## Phase 3 - Implement the .NET 10 worker supervisor

1. Add one immutable worker-pool generation per Dynamics profile generation.
   The generation owns all `Process`, named-pipe server/client, streams,
   cancellation sources/registrations, request maps, timers, health state, and
   recycle state.
2. Start workers with only non-secret bootstrap fields: pipe name, one-time
   nonce, protocol version, package-lock ID, and profile-generation ID. The pipe
   name is random, bounded, local-only, and ACL-restricted to the approved host
   identity.
3. Require a nonce-bound READY handshake before publication. Reject wrong
   executable hash, package-lock ID, worker kind, CE version, protocol version,
   nonce, or startup deadline. Always tear down the failed generation.
4. Integrate worker dispatch with the existing policy, audit intent,
   organization admission, runtime-host lease, deadline, cancellation, and
   idempotency boundaries. Queue entries hold an operation envelope, never a
   process/pipe/client/generation reference.
5. Implement replace-and-drain with at most one active and one draining worker
   generation. A queued request may bind only to an active generation with the
   identical operation revision; otherwise reject before Dynamics traffic.
6. Recycle workers by bounded policy:
   - maximum worker age;
   - maximum completed operation count;
   - private-bytes/working-set threshold;
   - health failure or protocol violation;
   - repeated timeout threshold;
   - package/profile generation replacement.
7. Recycling is graceful first, forced after a finite deadline. The supervisor
   must await stream loops, dispose registrations/timers/pipes/process handles,
   remove request-map entries, and prove all lifecycle counters return to zero.
8. Worker crash, malformed response, unexpected exit, or timeout returns a typed
   sanitized failure. It never replays an uncertain write and never falls back
   to Web API, Data8, another worker version, or another profile.

## Phase 4 - Verification before any consumer migration

### 4A. Deterministic local worker gates

1. Run protocol fuzz/boundary tests, project-reference/package allowlist tests,
   operation-registry tests, and secret-leak scans.
2. Run repeated worker start, READY, request, cancellation, timeout, graceful
   drain, forced kill, crash, malformed frame, and recycle tests.
3. Prove every worker generation returns process, pipe, stream, timer,
   cancellation registration, request-map, queue, admission permit, runtime
   lease, and strong-reference counters to zero after drain/termination.
4. Prove CE 8.2 and CE 9.1 workers can run simultaneously without loading each
   other's assemblies, sharing mutable state, or crossing profile/credential/
   result data.
5. Preserve and rerun the existing LocalDB cross-process capacity/fault suite.
   Worker pools must consume the same canonical organization budget rather than
   create a second capacity authority.

### 4B. Cross-process soak, fault, and performance gates

1. Run sustained read/query load with repeated process recycling and profile
   replacement. Capture managed heap, private bytes, working set, handles,
   threads, pipe count, active workers, queue depth, operation latency, and
   allocation rate.
2. Establish a post-warm-up baseline. Any unexplained sustained trend, retained
   worker/process/pipe/registration, session/profile cross-talk, or failure to
   return to the declared baseline blocks release.
3. Inject worker crash, hung SDK call, pipe break, malformed response, supervisor
   cancellation, Gateway restart, coordinator outage, slot expiry, and rapid
   profile reload. Prove bounded failure, no uncertain-write replay, no capacity
   spike, and deterministic cleanup.
4. Benchmark safe process-pool sizes and compare one-operation-per-worker with
   any proposed higher per-worker concurrency. Increase concurrency only when
   throughput improves without correctness, isolation, p95/p99, GC, handle, or
   memory regression.

### 4C. Locally hosted or deployed CE 8.2/9.1 compatibility gates

Run the actual website, Local or Central Gateway, and official workers on the
intended Windows host. A Visual Studio Local Gateway on the developer
workstation is a valid host; it must use the exact pinned worker and real
Organization Service, not a fake transport. For each target:

1. Start the exact pinned worker and verify sanitized Ready/identity evidence.
2. Execute the approved identity operation through the website/Gateway/worker
   path, not through direct Web API.
3. Execute representative read projections, paging, QueryExpression or
   server-owned FetchXML, metadata, and required actions/requests.
4. In a named non-production organization or test-owned records, execute a
   controlled create/update/delete or equivalent approved write and verify
   idempotency/rollback behavior.
5. Restart/recycle workers during traffic and verify graceful recovery, no
   cross-profile data, no session retention, and resource return to baseline.
6. Record only sanitized package-lock, worker kind, operation matrix, outcome,
   timing, and resource evidence. Do not record credentials, tokens, CRM bodies,
   connection strings, or raw server diagnostics.

This locally hosted or deployed compatibility test is not a D365APP01 administration channel. Do
not use Deployment PowerShell, reopen the IFD wizard, repeat Web API `WhoAmI`,
or inspect ASP.NET 1309 as a prerequisite. If the official worker produces
specific evidence of a server configuration defect, record a separate
operations incident without blocking unrelated local implementation work.

Phase 4 passes only when 4A and 4B pass and the required target profiles in 4C
pass. A CE 8.2-only or CE 9.1-only result may unlock only the matching profile;
it cannot imply the other version is supported.

## Phase 5 - First consumer and product migration

1. Select one bounded, read-heavy ChurchReport use case, initially the approved
   Package01 fee-read operation if its operation contract is complete.
2. Route it through ProductClient -> Gateway -> selected official worker behind
   `Package01FeeReadsEnabled`. Keep the flag false until its matching Phase 4
   profile gates pass.
3. Compare the official-worker result with the current legacy result only in a
   bounded, read-only shadow workflow that cannot duplicate writes or leak data.
4. Rollback changes the product feature flag back to the named legacy use case.
   It never changes transport inside one request and never falls back to Web API.
5. Migrate remaining use cases by explicit operation ID and matrix row. SDK
   types and generic Organization Service semantics never cross into the
   product contract.

## Phase 6 - Remove legacy routes and enforce the worker boundary

1. Remove `SpeechMessage.Dynamics.WebApi` from active routing, project
   references, solution build, smoke tests, scripts, configuration, and runtime
   factories after official workers replace its remaining test dependencies.
2. Remove direct-Web-API `WhoAmI` scripts/gates and archive only sanitized
   historical evidence outside the executable spec.
3. Remove the repository Data8 `PowerPlatform.Dataverse.Client` project and all
   ProjectReferences after CE 8.2 traffic migrates.
4. Remove product-side `Microsoft.PowerPlatform.Dataverse.Client`, direct
   Microsoft.Crm.Sdk.Proxy HintPaths, SDK-shaped pools/adapters/interfaces, WCF
   legacy code, and plaintext/fallback credential paths after their consumers
   migrate.
5. Enforce a generated source/project manifest:
   - Microsoft CRM SDK/XRM tooling packages/types are permitted only in the two
     explicit worker projects and worker-only tests;
   - every product, Gateway, shared contract, and ordinary test SDK reference is
     a build failure;
   - any SDK type in WorkerProtocol or Gateway public/internal contracts is a
     build failure;
   - any direct Web API transport selector, fallback, `/api/data/` root, or
     WebApi project reference in active production routing is a build failure.
6. Rotate legacy credentials and verify only approved worker-local secret
   references remain.

## Validation commands

Commands are finalized as the worker projects are added. The required shape is:

```powershell
dotnet build .\SpeechMessageProducts.sln --configuration Release
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --configuration Release `
  --filter "WorkerProtocol|WorkerBoundary|WorkerSupervisor|RuntimeIsolation|AdmissionCapacity"

# net48 worker-specific tests/builds use MSBuild/VSTest when required by the
# final project format and installed Visual Studio toolchain.

dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --configuration Release `
  --filter "WorkerSoak|WorkerFault|WorkerRecycle|NoLeak"

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Verify-DynamicsWorkerBoundary.ps1
```

The Local Gateway compatibility harness must select an explicit non-production or
approved target profile and fail before worker start if required secret
references, package locks, executable hashes, operation allowlists, or cleanup
ownership are absent.

## Release gates

| Phase | Gate | Failure condition |
| --- | --- | --- |
| 0 | Package and route selection | Worker package/hash not pinned, CE versions share an SDK graph, or Web API remains selectable. |
| 1 | Protocol and reference boundary | Oversized/malformed/unauthenticated frame accepted, secret enters args/env/log/IPC, or SDK type crosses the worker boundary. |
| 2 | Worker lifecycle | Official client/process/pipe/registration is not deterministically released, or worker admits an unregistered operation. |
| 3 | Supervisor/isolation | Worker generation leaks, wrong profile/version executes, capacity is duplicated, or failure falls back to another transport. |
| 4A | Local deterministic | Any protocol, boundary, lifecycle, crash/recycle, isolation, or cleanup assertion fails. |
| 4B | Soak/fault/performance | Sustained memory/handle/process/pipe growth, session/profile cross-talk, capacity spike, or unexplained post-drain retention. |
| 4C | Deployed compatibility | Required website -> Gateway -> official worker -> CE operation matrix fails for that exact profile. |
| 5 | Consumer migration | Shadow mismatch, unbounded latency, legacy bypass, or feature flag enables before matching Phase 4 evidence. |
| 6 | Legacy removal | Active WebApi/Data8/product SDK route remains, or SDK references exist outside the explicit worker allowlist. |

## High-risk rollback points

| Change | Rollback shape |
| --- | --- |
| Worker package update | Restore the prior immutable package-lock/executable generation and drain the failed generation. Never use binding redirects or mix versions. |
| Worker supervisor rollout | Disable the matching product feature flag and drain/terminate the worker generation. Preserve sanitized evidence. |
| Profile/credential rotation | Keep the last validated worker generation only within the approved credential window; confirmed revocation fails closed. |
| Product migration | Return only the named use case to its documented legacy implementation. Do not request-time fallback or select Web API. |
| Legacy project deletion | Restore the pre-removal commit only while correcting a missed consumer; do not reintroduce the removed route as a fallback. |

## Current next action

The production composition is now:

```text
Gateway
  -> ControlPlane / WorkerSupervisor
  -> separate CE 8.2 or CE 9.1 net48 Microsoft CrmServiceClient official-NuGet worker
  -> Dynamics Organization Service
```

Completed local execution checkpoint, refreshed on 2026-08-03:

1. The worker-neutral migration, fixed-snapshot Gateway deployment overlay,
   duplicate/secret/path/hash/GUID validation, and deterministic provider
   ownership are implemented. The overlay is optional, adjacent, higher
   precedence than checked-in placeholders, startup-only, and requires restart
   after change.
2. Fresh Release verification passed: Dynamics 411 passed / 0 failed / 7
   opt-in SQL skipped; the real fixed LocalDB gate then passed 8 / 8 with the
   selector restored in `finally`; focused worker lifecycle/fault/soak tests
   passed 73 / 73; CE 8.2 and CE 9.1 worker tests each passed 15 / 15; worker
   boundary verification reported zero findings; deployment/publish script tests
   passed.
3. The local reviewed publish artifacts were regenerated and independently
   matched their newly generated manifest. The manifest produced by the exact
   publication is the only deployment hash authority; a later publication may
   produce a different executable hash and must generate and verify a new
   manifest rather than reuse an earlier hash:
   - CE 8.2 executable SHA-256:
     `C39C2DE0D0C820D49164EA8F0E27F6B0A8343835347A402C430E8C93E385DAA0`;
   - CE 9.1 executable SHA-256:
     `6B09FA61422A72FE2EAADF28CB899F8B7827CFCC098B6B34FF45F1BDC968F637`;
   - CE 8.2 package-lock SHA-256:
     `4F49F64D7AD1075DE08DDF29C57317843A5BAD3CD0E6203CBC4AA3FF9BCCD58D`;
   - CE 9.1 package-lock SHA-256:
     `C2FF98918A505AB260676447B719F1EA52A7516028DBACAEF2B438C68F8383EC`.
4. A real deployment overlay was deliberately not generated. The generator
   requires the authoritative CE 8.2/9.1 Organization GUIDs, organization
   unique names, authentication/identity modes, credential-reference details,
   home realm where applicable, and stable Local Gateway host paths. These
   values must not be guessed merely to replace checked-in placeholders.
5. The feature-disabled ChurchReport local boundary is verified with the
   intended project content root: the browser reached the unsubmitted login
   page at `readyState=complete`, observed a form and password field, observed
   zero JavaScript errors and no `/v1` reference, and no login POST was issued
   by the verification. After closing the browser tab and stopping only the
   captured ChurchReport process, the local listener, Gateway/Worker/TestHost
   process set, and the 7244/57244 boundary listener set returned to zero.
6. `DynamicsProfileDefinition.CreateWorkerOptions()` now clones the validated
   immutable recycle-policy snapshot, matching the production runtime factory.
   A regression test first demonstrated the former silent fallback to default
   thresholds, then passed after the minimal copy fix; this protects consistent
   worker recycling without sharing mutable profile state.

Remaining ordered work:

1. Create a clean/versioned Local Gateway generation in a Visual Studio-owned
   local host directory only after the authoritative profile inputs and stable
   Local Gateway/worker paths exist. Publish workers first, start the Local
   Gateway second, then generate the overlay into that one local Gateway
   directory without overwrite or relocation.
2. Start and validate the real website -> localhost Gateway -> official worker
   -> CE 8.2 and CE 9.1 compatibility matrices, including identity, representative reads,
   test-owned writes, recycle, isolation, rollback, and resource-baseline
   evidence. Only this work can close Phase 4C for each exact profile.

D365APP01 IFD administration, HTTP `WhoAmI`, and direct Web API are not Phase 4
gates. Keep `Package01FeeReadsEnabled=false`; no unverified Phase 4 gate is
recorded as passed.
