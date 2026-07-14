# F06 Security Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Result And Exception Graphs Retain Sensitive Inputs

Notification failures copy the provider exception message and exception object
into the public result (`LineNotificationWorkflow.cs:47-81`). The result also
retains the recipient, retry key, and caller metadata
(`LineNotificationResult.cs:21-55,57-74`).

Reply failures do the same and retain the complete request by reference
(`LineReplyWorkflow.cs:50-84`, `LineReplyResult.cs:23-58`). That request
contains:

- a one-time reply token (`LineReplyRequest.cs:25`);
- the complete outbound message list (`:27`);
- mutable metadata (`:29`).

The throwing adapters place those result objects on public exceptions
(`LineNotificationException.cs:21-27`, `LineReplyException.cs:23-29`).
Therefore a normal exception logger, serializer, debugger, or long-lived job
record can traverse from the exception to provider text and workflow input.

A current B05 consumer logs the thrown exception and separately includes LINE
ID/retry-key values (`PaymentNotificationService.cs:128-135`). This proves
exception logging reachability. Static evidence does not prove that a reply
token has already been exfiltrated in production, so the confirmed claim is
retention and exposure through the public object graph, not observed token
theft.

The notification result also stores metadata by reference
(`LineNotificationResult.cs:38,58,74`), while the reply result stores the
entire mutable request by reference (`LineReplyResult.cs:30,37,49,58`).
Caller mutation after completion can change later diagnostic/log content.

Required boundary:

- public workflow result: status, stable error code, correlation/request ID,
  retry outcome, and a sanitized message;
- internal diagnostics: bounded provider detail and original exception through
  X02B logging;
- never retain a reply token or message graph in a public result;
- snapshot allowed metadata rather than keeping caller-owned dictionaries.

## Confirmed: Recipient Kind Is Not Enforced

`LineNotificationRecipient` stores a `Kind` and ID list
(`LineNotificationRecipient.cs:21-43`), but send uses only `PrimaryId`
(`LineNotificationWorkflow.cs:42-44`).

Validation checks:

- primary ID is nonblank (`LineNotificationWorkflow.cs:114-121`);
- `Users` has exactly one ID (`:123-130`).

It does not verify:

- user/group/room kind against the ID form;
- IDs beyond index zero except the `Users` count;
- leading/trailing whitespace;
- duplicate IDs;
- unexpected/malformed identifier shape.

Consequently `User(groupId)`, `Group(userId)`, or `Room(userId)` is transmitted
to the provider exactly as the supplied first ID. Because the provider push API
accepts user, group, and room recipients through one `to` field, a mislabeled
but otherwise valid ID can route content to a different audience class than the
workflow object declares. This is an accidental misdelivery/data-isolation
risk, not a demonstrated authorization bypass.

Required boundary:

- normalized immutable recipient value;
- one explicit destination for the single-recipient workflow;
- kind-specific format validation where the provider contract is stable;
- fail before any provider call when kind and ID do not agree;
- separate batch/multicast contract rather than a `Users` shape that is always
  rejected unless it contains one entry.

## Retry-Key Data Handling

The retry key is not a bearer credential, but it can encode business identifiers.
B05 currently builds a key containing payment/order identity and status
(`PaymentNotificationService.cs:78-96`), F06 stores it in results
(`LineNotificationResult.cs:25,34,47,58,70`), and B05 logs it
(`PaymentNotificationService.cs:130-135`).

The key should be an opaque UUID value and logs should use a bounded correlation
projection. This concern is ranked primarily under the retry/idempotency
contract because F06 does not itself emit logs.

## Guards And Counter-Evidence

- F06 rejects blank recipient IDs before provider I/O
  (`LineNotificationWorkflow.cs:114-121`).
- F06 rejects multiple `Users` entries instead of silently sending only the
  first (`LineNotificationWorkflow.cs:123-130`; test `:503-517`).
- Factory helpers validate several URLs, ranges, action counts, and null
  message objects before constructing SDK messages.
- No channel access token literal or credential lookup exists in F06.
- No evidence shows F06 sending exception details to a LINE recipient.
- The current result retention issue does not prove every consumer logs or
  serializes every property.

## Runtime Hypotheses

1. Generic structured exception logging serializes result/request properties.
2. Provider error text contains identifiers or request details in some cases.
3. Long-lived job/result storage extends the lifetime of message content and
   reply tokens.
4. Mutable metadata changes between workflow completion and later logging.
5. Kind/ID mismatches occur at integration boundaries.

These measurements refine impact. They do not negate the confirmed public
retention and unenforced recipient discriminator.
