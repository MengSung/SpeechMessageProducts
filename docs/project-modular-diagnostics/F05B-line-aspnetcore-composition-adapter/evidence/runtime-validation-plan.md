# F05B Runtime Validation Plan

Status: DEFERRED_UNTIL_BASELINE_AND_OPTIMIZATION_APPROVAL
Mode: DIAGNOSIS_ONLY

No restore, build, test, package, generation, formatting, migration, benchmark,
or runtime command was run. This plan is documentation only.

## Provider And Consumer Gate Prerequisites

1. Establish a green baseline for
   `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj`.
2. Establish consumer baselines for F04, F05A, F06, F07, and the X01 host
   startup/DI resolution tests.
3. Use capturing/fake HTTP handlers only. Never use production LINE
   credentials or endpoints.
4. Preserve existing workflow and RichMenu behavior while changing only
   registration, validation, and lifetime policy.

## Static Registration Contract Tests To Add

### Options And Security

1. Blank token fails startup validation.
2. Relative endpoint fails startup validation.
3. HTTP endpoint fails unless an explicit controlled-development override is
   enabled.
4. Default LINE HTTPS endpoint passes.
5. Explicit approved LINE HTTPS endpoint passes.
6. Explicitly allowed loopback test endpoint passes without broadening
   production policy.
7. Invalid options produce zero client-factory invocations and zero HTTP
   requests.
8. A capturing handler proves the bearer header is sent only to the validated
   destination.

### Idempotency And Override Policy

1. Calling `AddLineMessagingProcessor` twice produces one descriptor per
   intended direct capability.
2. `IEnumerable<ILineNotificationWorkflow>` and
   `IEnumerable<ILineReplyWorkflow>` contain one default implementation.
3. A pre-registered custom transport/processor/workflow is preserved according
   to the documented override policy.
4. A post-registration explicit replacement remains possible without
   implementation-specific knowledge.
5. Independent notification/reply registration does not register RichMenu.
6. Independent RichMenu registration does not register notification/reply.
7. Two trigger configuration calls compose mappings rather than delete the
   first call's mappings.
8. Product catalog registration does not duplicate base RichMenu services.

### Lifetime And Identity

1. Resolving notification and reply in one scope shares one client/processor
   lease.
2. Resolving profile and notification in one scope shares the same lease.
3. RichMenu capability uses the same scoped lease when enabled.
4. A new scope receives a new lease.
5. Scope disposal releases every tracked wrapper exactly once.
6. Root-provider resolution is rejected or explicitly documented.
7. No singleton captures F05A until its mutable legacy state is removed.
8. `ValidateOnBuild=true` and `ValidateScopes=true` pass.

## Runtime Measurements

### Multi-Capability Allocation

For one scope, resolve:

- notification only;
- notification plus reply;
- notification plus reply plus profile;
- all prior capabilities plus RichMenu.

Record:

- client-factory invocation count;
- `HttpClient` wrapper count;
- F05A processor count;
- allocation bytes;
- finalizable F05A objects;
- scope-disposal count.

Compare current transient fan-out with an approved scoped-lease design.

Acceptance target:

- one client/processor lease per scope;
- capability count does not multiply transport graphs;
- a new scope remains isolated;
- no process-global mutable processor state.

### Startup Cost

Measure service-registration and provider-build time for:

- current single registration;
- accidental repeated registration;
- granular idempotent registration.

Record descriptor count and options/configuration action count.

Acceptance target:

- stable descriptor cardinality after repeated convenience registration;
- no reflection/assembly scan;
- no network or file I/O;
- no meaningful regression in provider build time.

### Invalid Configuration

For blank token, relative endpoint, HTTP endpoint, and unapproved host:

- build/start the controlled test host;
- capture validation error;
- record client-factory and HTTP request counts.

Acceptance target:

- deterministic startup failure;
- zero HTTP requests;
- error identifies the invalid option without printing the token.

## Compatibility Matrix

- Existing `AddLineMessagingProcessor(Action<...>)` remains available as a
  compatibility bundle during migration.
- Existing default LINE endpoint remains unchanged.
- F06 notification/reply result models remain unchanged.
- F07 catalog, assignment, trigger, and provisioning behavior remains
  unchanged.
- X01 can migrate to granular registration one capability at a time.
- Controlled tests can use an explicit custom endpoint without weakening
  production validation.

## Rollback Boundaries

1. Add validation independently from lifetime changes.
2. Add granular registration extensions beside the compatibility bundle.
3. Make the compatibility bundle delegate to granular extensions.
4. Make registration idempotent before changing lifetimes.
5. Change transport/processor/workflows to scoped sharing in a separate step.
6. Retain transient compatibility factories if a background consumer cannot
   create scopes.
7. Do not change F04 HTTP serialization, F05A behavior, F06 result semantics,
   or F07 workflow/state logic in an F05B rollback.

## Pending Hypotheses

- exact per-request allocation savings;
- finalizer count under escaped transient processors;
- deployed custom endpoint usage;
- required credential rotation behavior;
- external consumers that rely on repeated-call or registration-order
  semantics.
