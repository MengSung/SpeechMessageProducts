# F06 Extraction Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Current Boundary

F06 contains useful product-neutral message factories and two small workflows,
but the reusable contracts are not yet cleanly separated:

1. message construction and factory validation;
2. recipient representation and validation;
3. notification/reply request shapes;
4. provider invocation through concrete F05A;
5. provider exception classification;
6. public result and throwing adapters.

The map explicitly assigns message factories, recipient validation, and result
normalization to F06
(`module-boundaries-and-optimization-map.md:142,742`).

## Retry And Idempotency Contract

`LineNotificationRequest.RetryKey` is an unrestricted string
(`LineNotificationRequest.cs:26`). F06 passes it unchanged
(`LineNotificationWorkflow.cs:44`), and F04 writes every nonblank value with
`TryAddWithoutValidation` (`LineMessagingClient.cs:167-180`).

The F06 test deliberately approves a colon-delimited non-UUID key
(`LineNotificationWorkflowTests.cs:467-481`). A current B05 consumer constructs
that shape (`PaymentNotificationService.cs:78-96`).

The result records only success/failure status and the original key
(`LineNotificationResult.cs:41-55,57-74`). It cannot represent:

- accepted duplicate;
- provider request ID or accepted request ID;
- retryable throttling metadata;
- timeout after possible transmission;
- definitely-not-sent versus delivery-ambiguous failure.

F04 owns HTTP status/header parsing. F06 owns validating the workflow-level
idempotency input and exposing a delivery outcome suitable for product retry
policy. Automatic retry is intentionally not recommended: reply tokens are
one-time, and push replay without a valid idempotency contract can duplicate
delivery.

## Message Contract

`LineNotificationContent.SdkMessagesList` rejects only null/empty collections
(`LineNotificationContent.cs:211-223`). Reply validation does the same
(`LineReplyWorkflow.cs:117-124`).

F04 documents a maximum of five messages
(`ILineMessagingClient.cs:34-35,58-67`). F06 does not reject:

- six or more messages;
- a null message element;
- a mutable caller-owned collection whose elements change before send.

The clean F06 boundary should create an immutable one-to-five-message batch,
validate all elements before provider invocation, and reuse that value for both
notification and reply workflows.

Factory-specific guards already exist for required strings, HTTPS URLs,
coordinates, counts, and ranges. The issue is the final outbound batch
contract, not absence of all message validation.

## Recipient Contract

`LineNotificationRecipient` combines an enum discriminator with a list of IDs
(`LineNotificationRecipient.cs:21-43`). The single-recipient workflow then
uses only `PrimaryId` (`LineNotificationWorkflow.cs:42-44`) and rejects
`Users` only when the list count is not one (`:123-130`).

Recommended shape:

- `LineDestination` or equivalent immutable single destination:
  `{ kind, normalized id }`;
- kind/ID consistency and whitespace validation at construction;
- no list on a single-recipient contract;
- separate explicit multicast/fan-out workflow with bounded batches and
  per-recipient results if required later.

## Result Normalization And Dependency Seam

Notification and reply workflows duplicate almost the same provider exception
matrix:

- notification: `LineNotificationWorkflow.cs:47-81`;
- reply: `LineReplyWorkflow.cs:50-84`.

They differ in error-code prefixes and result shapes:

- notification exposes recipient/retry/metadata
  (`LineNotificationResult.cs:21-74`);
- reply exposes the full request (`LineReplyResult.cs:23-58`);
- both expose raw exception message and exception;
- both use the same notification status enum.

Both workflows depend directly on `LineMessagingProcessorClass`
(`LineNotificationWorkflow.cs:25-29`,
`LineReplyWorkflow.cs:27-31`). F05A owns the reusable processor capability
interface, but F06 owns consuming a narrow send/reply capability rather than
the whole compatibility class and applying one consistent workflow outcome
normalizer.

Recommended modules:

### 1. Message Batch

Input: message factory output or SDK message sequence.

Contract:

- immutable one-to-five non-null messages;
- one validation path shared by push and reply;
- no second serialization in F06.

### 2. Recipient

Input: kind plus provider ID.

Contract:

- normalized immutable single destination;
- explicit kind/ID validation;
- safe diagnostic projection that does not expose the complete ID by default.

### 3. Delivery Outcome

Input:

- workflow operation;
- sanitized F05A/F04 provider result;
- optional idempotency key/correlation.

Output:

- succeeded;
- validation failed;
- accepted duplicate;
- provider rejected;
- provider unavailable;
- caller cancelled;
- timed out;
- delivery ambiguous;
- unexpected internal failure.

The public outcome must not retain reply tokens, message graphs, raw
exceptions, or caller-owned metadata.

### 4. Provider Capability

F05A-owned narrow capability consumed by F06:

```text
send(destination, message batch, retry UUID, cancellation)
reply(reply token, message batch, cancellation)
  -> provider outcome/correlation
```

F06 remains responsible for workflow validation and result classification.
F04 remains responsible for HTTP, serialization, provider headers/status, and
transport exceptions.

## Test Seam

Current tests construct:

```text
capturing handler -> HttpClient -> concrete F04 client
  -> concrete F05A processor -> F06 notification workflow
```

(`LineNotificationWorkflowTests.cs:537-540`).

This proves real payload shape but provides no isolated F06 seam. The suite has
no reply workflow test. Extraction should retain selected provider-contract
tests while adding fake-capability unit tests for recipient, message,
idempotency, cancellation, and result normalization.

## Rejected Or Narrowed Extraction Claims

- Missing automatic retry: rejected. Unconditional replay is unsafe.
- Duplicate serialization: rejected. F04 serializes once per operation.
- Repeated network calls: rejected. Each F06 operation makes one provider call.
- `ToList` as a standalone performance issue: rejected; bounded-copy cost is
  negligible after enforcing the five-message limit.
- RichMenu integration: excluded; F07-owned.
- Profile/CRM lookup: excluded; B07/F03A-owned.
- Processor credential/lifetime defects: excluded; F05A/F05B-owned.
- SDK transport/error-header parsing: excluded; F04-owned.

## Cross-Module Handoffs

1. F04: typed provider correlation, accepted-duplicate, throttle, ambiguous
   transmission, and cancellation-capable HTTP contracts.
2. F05A: narrow send/reply capability over F04 with cancellation.
3. F05B: register the narrow capabilities and workflows.
4. B04C/B05/B07: supply product idempotency policy and consume sanitized
   delivery outcomes.
5. X02B: internal diagnostics and redaction.
6. X02C: cancellation, allocation, and batch measurements.
