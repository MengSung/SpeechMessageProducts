# F06 Performance Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Caller Cancellation Cannot Reach Provider Work

The public workflow interfaces have no `CancellationToken`:

- notification: `ILineNotificationWorkflow.cs:21-23`;
- reply: `ILineReplyWorkflow.cs:24-26`.

The implementations make tokenless F05A calls:

- notification: `LineNotificationWorkflow.cs:42-44`;
- reply: `LineReplyWorkflow.cs:44-46`.

`TaskCanceledException` is always normalized as provider timeout/unavailability
(`LineNotificationWorkflow.cs:65-72`, `LineReplyWorkflow.cs:68-75`).
Because no caller token exists, the result cannot distinguish caller
cancellation from an F04/F05A timeout even after lower layers gain
cancellation.

Control/lifetime flow:

```text
request abort / shutdown / job cancellation
  -> F06 API has no token
  -> F05A/F04 provider call starts without caller cancellation
  -> network work continues until provider completion or HttpClient timeout
  -> any TaskCanceledException is labeled provider timeout
```

F04 and F05A currently also lack the needed transport overloads. F06 still owns
exposing cancellation on its workflow contract, forwarding it when the lower
capabilities are available, and preserving caller-cancelled versus
provider-timeout outcomes.

## Message Construction And Serialization

No repeated F06 serialization was found.

- notification SDK messages are copied once with `ToList`
  (`LineNotificationWorkflow.cs:154-161`);
- reply messages are copied once with `ToList`
  (`LineReplyWorkflow.cs:44-46`);
- F05A forwards each operation once
  (`LineMessagingProcessorClass.cs:317-351`);
- F04 serializes each push/reply request once
  (`LineMessagingClient.cs:432-437,559-565`).

The list copies are bounded to five once the missing message-count contract is
enforced. They are not a standalone optimization target.

## Network Call And Retry Review

Each F06 operation performs at most one provider call:

- notification: one `SendMessagesAsync` call
  (`LineNotificationWorkflow.cs:40-45`);
- reply: one `ReplyMessagesAsync` call (`LineReplyWorkflow.cs:42-48`).

F06 has no internal retry loop. This avoids duplicate replay of one-time reply
tokens and avoids accidental duplicate push delivery. The defect is not
"missing automatic retries"; it is the lack of a validated idempotency key and
typed accepted-duplicate/ambiguous outcome.

No F06 N+1 loop exists. `LineNotificationRecipient.Users` is explicitly
rejected unless it contains exactly one ID
(`LineNotificationWorkflow.cs:123-130`). Multi-recipient batching, multicast,
or fan-out policy must be a separate contract with cancellation, partial
results, and provider-limit handling; it should not be added as an implicit
loop inside the current single-recipient workflow.

## Avoidable Provider Rejections

F06 validates only nonempty message lists:

- notification factory: `LineNotificationContent.cs:211-223`;
- reply workflow: `LineReplyWorkflow.cs:117-124`.

It does not enforce maximum five messages or reject null elements. F05A repeats
only the nonempty check (`LineMessagingProcessorClass.cs:324-327,346-349`).
Invalid lists therefore reach serialization/network I/O and are rejected by
the provider or fail later. This is ranked as an extraction/contract issue
because the main benefit is deterministic local validation; the saved
serialization/network cost is secondary.

## Counter-Evidence

- Product-friendly message factories generally construct one message and reuse
  shared validation helpers.
- F06 does not create a processor/client per operation; lifetime is supplied by
  the consumer/composition layer.
- No blocking `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` exists in F06
  production source.
- B05 has a synchronous bridge, but that consumer behavior is outside F06.
- No repeated provider profile lookup, CRM query, RichMenu call, or message
  serialization is owned by this leaf.

## Runtime Measurements Pending

1. Time from caller cancellation to active handler observation.
2. Number of active provider calls after request abort or host shutdown.
3. Provider-call count and elapsed time for invalid message lists.
4. Allocation difference between current request retention and sanitized
   immutable result projections.
5. Throughput and partial-failure behavior for any future explicit batching
   workflow.

No benchmark, build, test, or network call was run.
