# F02 Runtime Validation Plan

Status: COMPLETE
Runtime-pending confirmed issues: 0
Mode: DIAGNOSIS_ONLY

## Approval Effect

No retained issue depends exclusively on runtime evidence. Missing disposal,
repeated metadata loads, unbounded quotas/loops, unvalidated metadata
redirection, and the collapsed construction boundary are statically visible.

The following commands and measurements are future acceptance work only. This
diagnostic and its CCG reviewers must not execute them because restore, build,
test, generation, and generated-output writes are strictly prohibited.

## Required Future Gate

Executor: future approved F02 implementation task with F03A/F03Q/X01 consumer
owners.

Environment:

- disposable clean clone or approved CI branch;
- approved .NET 10 SDK and package sources;
- fake CRM/STS endpoints using non-production credentials;
- no production token, password, tenant, or PII in logs/results.

Provider/consumer commands after implementation approval:

```powershell
dotnet restore PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj
dotnet build PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj --no-restore --configuration Release
dotnet test <future-F02-test-project> --no-build --configuration Release
dotnet build ToolUtility/ToolUtility.csproj --no-restore --configuration Release
dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --no-restore --configuration Release
```

These commands are documented, not executed.

## F02-PERF-001 Acceptance

Method:

1. Use fake AD and federation transports plus communication-state probes.
2. Repeatedly create, use, fault, replace, and dispose leases.
3. Record open channels/factories, sockets, authentication contexts, and
   process handles before/after forced GC only as secondary evidence.
4. Verify healthy WCF objects close and faulted objects abort.

Pass threshold:

- every successful client creation has one deterministic owner;
- dispose is idempotent;
- no live channel/factory/auth context remains after pool replacement or host
  shutdown;
- credentials/tokens are not logged;
- the compatibility adapter remains usable as `IOrganizationService`.

Failure effect: keep F02-PERF-001 and block consumer migration.

## F02-SEC-001 Acceptance

Method:

1. Serve oversized WSDL, high-fan-out imports, large XML fields, slow bodies,
   and endless-but-valid AD negotiation transcripts.
2. Exercise both AD and federation paths under explicit finite policy.
3. Record elapsed time, peak memory, requests/imports followed, and exception
   classification.

Pass threshold:

- deterministic rejection at configured byte/import/depth/round/deadline
  limits;
- no `Int32.MaxValue` production quota;
- cancellation/deadline stops the whole construction/authentication flow;
- normal captured CRM/STS fixtures remain compatible.

Failure effect: keep F02-SEC-001.

## F02-SEC-002 Acceptance

Method:

1. Supply same-origin, cross-origin, HTTP downgrade, loopback, link-local,
   private-address, redirect, and explicitly allowed on-prem metadata fixtures.
2. Verify imports resolve relative to the source document.
3. Confirm credentials are applied only after endpoint validation.

Pass threshold:

- default policy rejects unapproved cross-origin/downgrade/internal targets;
- approved on-prem exceptions are explicit and auditable;
- rejected endpoints receive no credentials or authenticated SOAP request;
- validation errors identify policy category without exposing secrets.

Failure effect: keep F02-SEC-002.

## F02-PERF-002 Acceptance

Method:

1. Capture three clean samples for current construction of 3 and 20 clients
   against a fake metadata graph.
2. Implement bounded immutable metadata caching with single-flight population.
3. Repeat cold, warm, expiry, concurrent, and failure cases.

Pass threshold:

- one metadata fetch graph per normalized key during a valid cache window;
- no credential/token/channel object is cached;
- failures do not poison the cache;
- warm construction materially reduces metadata requests and XML allocations,
  target at least 50% construction-time improvement for three same-endpoint
  clients in the controlled fixture.

Failure effect: retain structural issue and remove the cache if correctness or
identity isolation regresses.

## F02-EXT-001 Acceptance

Method:

1. Add provider fixtures for metadata, authentication, transport states, and
   consumer compatibility.
2. Prove each layer can be tested without a live CRM endpoint.
3. Compile F03A/F03Q and the host through the compatibility adapter.

Pass threshold:

- explicit resolver, authentication-session, and transport-lease ownership;
- dependency direction remains F02 to consumers;
- F03A query behavior does not move into F02;
- each extraction step is independently reversible;
- required provider and consumer gates are green.

Failure effect: retain F02-EXT-001 and do not remove the compatibility path.

## Rejected Candidate Validation

- CallerId concurrency: add a two-lease/two-identity fixture; promote only if
  a reachable shared-client path crosses identities.
- NSspi lifecycle: compare package/compile inventory after an explicit support
  decision; promote only on a current build, packaging, consumer, or material
  cost failure.
- Retry policy: use transient-fault fixtures and promote only if actual
  amplification, unsafe replay, or missing recovery is demonstrated.
