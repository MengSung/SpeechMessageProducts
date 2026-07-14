# F05A Runtime Validation Plan

Status: DEFERRED_UNTIL_BASELINE_AND_OPTIMIZATION_APPROVAL
Mode: DIAGNOSIS_ONLY

No restore, build, test, package, generation, formatting, migration, or
benchmark command was run. This plan is documentation only.

## Provider Gate Prerequisites

1. Establish a green baseline for
   `LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj`.
2. Confirm the explicit credential subject test remains executable under its
   F04 test container or move it to the canonical F05A test container without
   changing ownership.
3. Use fake/capturing handlers only; never use production LINE credentials or
   endpoints.
4. Add consumer gates for F05B, F06, F07, and relevant B05/B07 tests before any
   contract change is declared complete.

## Static Contract Tests To Add

### Lifetime

1. A token-created processor disposes its owned F04 client exactly once.
2. A configuration-created processor disposes its owned client exactly once.
3. An injected client is never disposed by F05A.
4. Repeated processor `Dispose` calls are idempotent.
5. No finalizer is required after deterministic ownership is implemented.
6. Current `using` consumer behavior actually reaches the owned client.

### Cancellation

1. Push cancellation reaches the capturing handler.
2. Reply cancellation reaches the capturing handler.
3. Profile cancellation reaches the capturing handler.
4. RichMenu list/create/upload/link cancellation reaches the capturing handler.
5. F07 distinguishes caller cancellation from provider timeout.
6. B07 profile cancellation can interrupt an in-flight provider call.

### Credential Contract

1. Blank token fails before any HTTP call for every operation family.
2. F05B option validation fails service startup/resolution deterministically.
3. Clean F05A interfaces do not read current-directory `appsettings.json`.
4. Explicit reconstruction can adopt a rotated credential; no process-global
   stale token remains in the clean path.
5. Legacy constructor precedence remains compatible until removed.

### Security And Event Compatibility

1. Provider exceptions never appear in outbound user messages.
2. One failure produces at most one stable user-facing notification.
3. Internal logs receive a correlation code and sanitized exception through
   X02B.
4. Malformed/null/unsupported dynamic events produce no provider side effect.
5. Postback parsing rejects missing/duplicate/unexpected fields without array
   indexing failures.
6. Parallel event-adapter calls do not share user/message state.

### Extraction

1. F06 can use a fake message/reply capability without concrete F04/F05A.
2. F07 can use the F05A RichMenu capability without its local pass-through
   adapter.
3. F05B registers narrow interfaces plus compatibility class during migration.
4. Legacy confirmation-code message text remains byte-for-byte compatible in
   the legacy adapter.
5. The clean profile contract returns the F04 DTO without a second mapping
   allocation.

## Runtime Measurements

### Client Lifetime

Under a local loopback endpoint:

1. construct/use/dispose token-created processors for 1, 10, 100, and 1,000
   iterations;
2. record active handlers/sockets, disposal calls, finalizable objects, and
   time to steady state;
3. compare current behavior with explicit owned disposal;
4. compare token-created construction with F05B `IHttpClientFactory` reuse.

Acceptance target:

- owned client disposed once per owner;
- injected client disposal count remains zero from F05A;
- stable handler/socket count after steady state;
- no F05A finalizer queue population.

### Cancellation

Use a handler that blocks until cancellation:

- measure time from caller cancellation to handler observation;
- run push, reply, profile, list, create, upload, and link operations;
- cancel in the middle of an F07 multi-definition synchronization.

Acceptance target:

- cancellation reaches the active provider call within a bounded scheduling
  interval;
- no later provider step begins;
- caller cancellation is not reported as provider timeout.

### Invalid Credential

With an empty token and a counting handler:

- execute every operation family;
- record request count and elapsed time.

Acceptance target:

- zero HTTP requests;
- deterministic configuration/argument failure;
- no provider-rejected result for a local missing configuration.

### Exception Disclosure

Inject synthetic exceptions containing:

- fake provider response text;
- fake internal path/stack;
- fake secret-like marker.

Capture all outbound messages and logs.

Acceptance target:

- no exception detail or marker in user messages;
- stable public error message;
- sanitized internal log contains correlation, not credentials.

## Performance And Compatibility Baselines

Collect:

- allocations per pass-through call;
- HTTP request count;
- serialization count in F04;
- legacy DTO allocations;
- RichMenu provider call count per workflow;
- cancellation latency;
- handler/socket count;
- finalizer count.

Expected invariant:

- F05A extraction must not add a second JSON serialization;
- one F05A operation maps to one intended F04 provider call;
- F06/F07 workflow result semantics remain unchanged.

## Rollback Boundaries

1. Add narrow interfaces beside `LineMessagingProcessorClass`.
2. Correct ownership/disposal independently from interface migration.
3. Add cancellation overloads while retaining old overloads using
   `CancellationToken.None`.
4. Migrate F06, F07, F05B, B05, and B07 separately.
5. Move product binding/error helpers behind a legacy adapter only after B07
   consumer tests pass.
6. Remove ambient/default constructors and duplicate DTO only after external
   consumer review.
7. Do not change F04 serialization or F06/F07 workflow result semantics as part
   of an F05A rollback.

## Pending Hypotheses

- socket/handler retention magnitude;
- finalizer queue impact;
- deployed exception detail content;
- cancellation delay under provider slowness;
- external consumers outside the repository;
- frequency of empty-token fallback in current static workflow factories.
