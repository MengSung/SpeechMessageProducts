# F05A Performance Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Owned Client Disposal Is Lost

The token constructor creates F04's obsolete internally-owned client
(`LineMessagingProcessor/LineMessagingProcessorClass.cs:45-51`).
F04 documents that constructor as not recommended for production, creates a new
`HttpClient`, and marks it for disposal
(`Line.Messaging/LineMessagingClient.cs:117-131`).

F05A implements `IDisposable`, but its dispose path releases nothing
(`LineMessagingProcessor/LineMessagingProcessorClass.cs:132-155`).
F04 releases the internal client only when its own `Dispose` is called
(`Line.Messaging/LineMessagingClient.cs:2823-2828`).

Confirmed lifetime flow:

```text
token/config constructor
  -> F05A creates F04 LineMessagingClient
  -> F04 creates owned HttpClient
  -> F05A Dispose/finalizer
  -> no F04 Dispose
  -> no deterministic handler/socket cleanup
```

Current evidence:

- a controller uses `using` around a token-created processor
  (`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:607-619`);
- two static workflow factories create token-owned processors without an
  exposed disposal path
  (`SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:136-165`,
  `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:334-337`).

Counter-evidence:

- the injected F04 constructor is externally owned and must not be disposed by
  F05A (`Line.Messaging/LineMessagingClient.cs:107-115`);
- F05B uses `IHttpClientFactory` and injects that non-owning client
  (`LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:54-63`);
- actual socket/handler accumulation magnitude was not measured.

The unnecessary finalizer also places undisposed processor instances on the
finalization path despite F05A directly owning no unmanaged resource.

## Confirmed: Cancellation Stops Only Between Provider Calls

F05A has no `CancellationToken` parameter on send, reply, RichMenu, or profile
operations (`LineMessagingProcessorClass.cs:255-653`).

Consumer evidence:

- F07 `SyncAsync` accepts cancellation
  (`LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:74`);
- it checks the token inside its definition loop
  (`LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:103-106`);
- provider calls such as `GetRichMenuListAsync`, `CreateRichMenuAsync`, and
  upload cannot receive the token
  (`LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:82,194,199`);
- F07 assignment accepts cancellation but calls provider through tokenless
  delegates
  (`LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:97-100,136-137,179-196`);
- B07 profile provider checks cancellation at line 63 and then calls a
  non-cancellable F05A operation at line 64
  (`SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:56-65`).

Confirmed flow:

```text
caller cancellation
  -> local ThrowIfCancellationRequested / local store cancellation
  -> F05A provider call without token
  -> wait for provider completion or HTTP timeout
  -> next local cancellation check
```

Impact is bounded by current HTTP timeout/provider duration, but cancelled
requests and host shutdown cannot terminate the active provider call.

F04's current public interface also lacks cancellation on the relevant methods
(`Line.Messaging/ILineMessagingClient.cs:35-67,240`), so end-to-end correction
requires an F04 handoff. F05A still owns exposing the processor contract that
consumers need.

## Serialization And Parsing Review

No duplicate provider serialization was found in F05A:

- F05A creates typed `TextMessage`/message lists and calls F04
  (`LineMessagingProcessorClass.cs:280-281,305-306,329,351`);
- F04 serializes the push/reply payload once
  (`Line.Messaging/LineMessagingClient.cs:432-437,559-565`).

The legacy `GetUserProfile` allocates a second DTO and copies four fields after
F04 deserialization (`LineMessagingProcessorClass.cs:592-603`). This is
compatibility overhead but not a material standalone performance issue because
repository search found only legacy/internal usage and one test.

`ParsePostBackString` performs repeated `Split` allocations and fixed-position
indexing (`LineMessagingProcessorClass.cs:699-710`), but its only caller is the
unreferenced dynamic dispatcher. It is not promoted as an active hot path.

## Dispatch Loop Review

F05A contains no active recipient, event-collection, or RichMenu catalog loop.

- F07 owns catalog/assignment loops.
- ChurchReport owns sequential profile-refresh loops.
- F04 owns request serialization and network transport.

No F05A N+1 or repeated-network loop was retained.

## Compatibility Wrapper Overhead

Most F05A methods are `async` pass-through wrappers that await one F04 task and
return no additional transport result. This creates small state-machine
overhead across many methods, but it is not ranked independently because:

- network latency dominates;
- some wrappers perform validation or DTO conversion;
- the larger issue is the absence of narrow interfaces and cancellation.

Direct task returns can be considered during contract extraction, but not as a
primary optimization target.

## Runtime Hypotheses

1. Repeated token-created processors retain active handlers/sockets until
   connection/runtime cleanup.
2. Finalizer queue pressure is visible in legacy construction-heavy flows.
3. Cancelled RichMenu/profile operations continue for the current provider-call
   duration.
4. Empty-token workflows incur external request latency before returning a
   provider rejection.

No benchmark, build, or test was run.
