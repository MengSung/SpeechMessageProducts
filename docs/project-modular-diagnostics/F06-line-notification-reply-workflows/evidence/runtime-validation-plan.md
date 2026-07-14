# F06 Runtime Validation Plan

Status: DEFERRED_UNTIL_BASELINE_AND_OPTIMIZATION_APPROVAL
Mode: DIAGNOSIS_ONLY

No restore, build, test, package, generation, formatting, migration, benchmark,
coverage, or external LINE call was run. This file is a future validation plan,
not authorization to implement or execute it.

## Gate Prerequisites

1. Establish a green F04 SDK provider baseline.
2. Establish a green F05A processor baseline with a narrow fakeable
   send/reply capability.
3. Establish the F06 workflow baseline.
4. Prepare F05B, B04C, B05, and B07 consumer gates.
5. Use synthetic recipient IDs, reply tokens, retry UUIDs, metadata, messages,
   and provider errors only.
6. Use a capturing/blocking handler; never call the external LINE API.

## Recipient Contract Tests

1. Valid user, group, and room destinations make one intended provider call.
2. User kind rejects group/room ID forms before provider I/O.
3. Group and room kinds reject user ID forms before provider I/O.
4. Leading/trailing whitespace is rejected or normalized by one documented
   rule.
5. Blank, malformed, duplicate, and multi-ID single-destination inputs fail
   locally.
6. A future batch contract enforces provider batch limits and returns
   per-recipient results without silently selecting index zero.

## Message Batch Tests

For notification and reply:

1. one through five non-null messages are accepted;
2. zero messages fail locally;
3. six messages fail locally;
4. any null element fails locally;
5. caller mutation after request construction cannot change the validated
   outbound batch;
6. each operation produces one serialization and one provider call;
7. factory-specific URL/range/action validation remains unchanged.

## Retry And Idempotency Matrix

Notification:

- null key -> no retry header;
- valid UUID -> one retry header;
- blank or malformed key -> local validation failure;
- 200/202 -> succeeded with provider correlation;
- 409 accepted duplicate -> explicit accepted-duplicate outcome;
- 400/401/403/404 -> provider-rejected outcome;
- 429 -> throttle/retry metadata without automatic replay;
- 500/502/503/504 -> transient provider failure;
- timeout before transmission -> definitely not sent when provable;
- timeout/disconnect after possible transmission -> delivery ambiguous while
  preserving the opaque retry UUID.

Reply:

- no automatic retry;
- reused/expired token maps to provider rejected;
- ambiguous timeout does not replay a one-time token;
- result and exception never retain the reply token.

## Cancellation Tests

Use a handler that blocks until cancellation:

1. notification cancellation reaches the active provider call promptly;
2. reply cancellation reaches the active provider call promptly;
3. caller cancellation maps to `CallerCancelled`, not provider timeout;
4. provider timeout maps separately from caller cancellation;
5. no later provider call starts after cancellation;
6. `SendOrThrowAsync` and `ReplyOrThrowAsync` preserve cancellation semantics
   rather than wrapping caller cancellation as provider failure.

## Result Sanitization And Snapshot Tests

Inject synthetic values containing secret-like markers:

- reply token;
- recipient ID;
- message content;
- metadata value;
- provider body;
- internal exception path/stack.

Assertions:

1. public notification/reply results contain no reply token, full message
   graph, raw exception, or unbounded provider body;
2. public exceptions contain only stable sanitized message, error code, and
   correlation;
3. internal X02B diagnostics receive bounded sanitized detail separately;
4. metadata is allowlisted and snapshotted;
5. caller mutation after completion cannot alter a stored result;
6. generic structured exception logging does not traverse sensitive request
   objects.

## Outcome Normalization Tests

One table-driven suite should cover both notification and reply:

- validation failed;
- succeeded;
- accepted duplicate where applicable;
- provider rejected;
- provider unavailable;
- caller cancelled;
- timed out;
- delivery ambiguous;
- unexpected internal failure.

The same provider failure must produce the same category and correlation shape
across both workflows. Operation-specific error codes may differ only where the
caller needs to distinguish push from reply.

## Performance Measurements

Capture:

- cancellation latency;
- active requests after caller abandonment;
- provider-call and serialization count;
- allocation retained by current result/request graphs versus sanitized
  snapshots;
- invalid-input elapsed time and request count;
- future batch throughput, partial-failure count, and cancellation stop point.

Expected invariants:

- one F06 operation maps to one intended provider call;
- no automatic retry is introduced;
- no second JSON serialization is added in F06;
- invalid recipient/message/retry input produces zero provider calls;
- cancellation stops the active call and prevents subsequent batch work.

## Consumer Gates

After F04/F05A/F06 provider and workflow tests:

1. F05B service-resolution and registration tests.
2. B04C notification consumer tests.
3. B05 payment notification/idempotency tests.
4. B07 push/reply facade and webhook reply tests.
5. Host compile and selected integration tests.

## Rollback Boundaries

1. Add immutable recipient/message/result types beside current types.
2. Add cancellation overloads while current overloads delegate with
   `CancellationToken.None`.
3. Add valid UUID retry-key validation before changing result semantics.
4. Add typed outcomes beside legacy result properties/adapters.
5. Migrate notification and reply independently but share one normalizer.
6. Migrate F05B/B04C/B05/B07 one consumer at a time.
7. Keep legacy exception adapters until consumer inventory is complete.

## Pending Runtime Hypotheses

- frequency of invalid recipient kind/ID combinations;
- actual provider error-body sensitivity;
- whether structured logging serializes public result properties;
- rate of ambiguous push outcomes and accepted duplicates;
- cancellation delay under provider slowness;
- demand and optimal limits for multi-recipient batching.
