# F03B Runtime Validation Plan

Status: DEFERRED_UNTIL_GATE_REPAIR_AND_OPTIMIZATION_APPROVAL
Mode: DIAGNOSIS_ONLY

No restore, build, test, package, generation, formatting, migration, or
benchmark command was run. The current `ToolUtility.Tests` net8/net10 and
solution-enrollment gate blocks executable validation.

## Provider Gate Prerequisites

1. F01A/F01D enroll a canonical ToolUtility test project compatible with
   `ToolUtility` net10.0.
2. F03B fixes the subject-test constructor mismatch at
   `ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs:30-33`.
3. Tests use fakes/capturing handlers and never use production CRM or LINE
   credentials.
4. B07 provides consumer-level tests for the migrated `LineNotifyUtility`
   behavior.

## Static Contract Tests To Add

1. Text push does not persist full content unless an explicit audit policy
   requests it.
2. Audit occurs after confirmed delivery, or records explicit failed/pending
   status rather than a sent-shaped record.
3. Recipient identifiers are minimized or transformed according to policy.
4. Multicast uses one bounded/batched audit operation rather than per-recipient
   lookup/create.
5. Every message kind returns the same typed delivery result and failure
   classification.
6. Retry key and cancellation reach the F04/F06 dependency.
7. Externally injected clients are not disposed by the adapter; adapter-owned
   clients are impossible or explicitly owned.
8. Legacy RichMenu methods are absent from the clean adapter or delegate to F07
   with ownership checks.
9. Entity schema tests prove there is one canonical audit contract, not both
   `letter` and `linemessage`.

## Performance Measurements

For recipient counts `1, 10, 100, 500`, capture:

- CRM lookup count;
- CRM create/batch count;
- LINE request count;
- time before LINE request begins;
- total latency;
- allocation and serialized payload size;
- concurrent task count and unobserved exception count.

Acceptance target for the proposed contract:

- one LINE multicast request;
- zero per-recipient CRM lookups on the delivery path;
- at most one bounded audit batch after delivery;
- cancellation observed before audit continuation;
- no fire-and-forget consumer calls.

## Client-Lifetime Measurement

Under a controlled fake/loopback endpoint:

1. construct and release repeated B07 consumer instances;
2. record active sockets/handlers and disposal calls;
3. compare token-only client construction with DI-managed injected
   `HttpClient`;
4. confirm stable socket/handler count after steady state.

This measures the runtime consequence of the confirmed ownership defect without
claiming socket exhaustion in advance.

## Security/Privacy Validation

With synthetic messages only:

- inspect captured CRM entities for message body, recipient ID, delivery state,
  and timestamps;
- verify failed LINE sends cannot create successful audit records;
- verify configured redaction/minimization;
- obtain human policy confirmation for retention and CRM role access.

## Rollback Boundaries

1. Add the new typed adapter beside legacy `PushUtility`.
2. Migrate the B07 consumer independently.
3. Switch audit persistence independently after dual-write comparison using
   synthetic data.
4. Retire legacy RichMenu methods only after F07/B07 consumer inventory.
5. Preserve F04 SDK behavior; rollback never requires changing F04 protocol
   serialization.

## Pending Runtime Hypotheses

- linear multicast latency magnitude;
- socket/handler accumulation magnitude;
- actual rate of unobserved delivery failures;
- production CRM ACL and retention effectiveness;
- existence of dormant external consumers not visible in this repository.
