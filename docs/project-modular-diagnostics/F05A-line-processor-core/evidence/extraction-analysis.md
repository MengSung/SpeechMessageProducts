# F05A Extraction Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Current Boundary

F05A is physically isolated as a project but not contractually cohesive. Its
sole implementation class combines:

1. ambient JSON/environment credential discovery
   (`LineMessagingProcessor/LineMessagingProcessorClass.cs:35,40-43,90-118`);
2. concrete SDK construction and lifetime
   (`LineMessagingProcessorClass.cs:31-33,45-59`);
3. public mutable legacy event state and dynamic dispatch
   (`LineMessagingProcessorClass.cs:37-38,158-253`);
4. push/reply transport
   (`LineMessagingProcessorClass.cs:255-352`);
5. 15 RichMenu provider operations
   (`LineMessagingProcessorClass.cs:361-563`);
6. user/group/room profile transport
   (`LineMessagingProcessorClass.cs:575-653`);
7. product-specific binding URL/message/error behavior
   (`LineMessagingProcessorClass.cs:655-696`);
8. postback parsing and a duplicate profile DTO
   (`LineMessagingProcessorClass.cs:699-729`).

The project name says processor core, but the public boundary is a compatibility
class plus product remnants.

## Dependency Direction

F04 already exports `ILineMessagingClient`
(`Line.Messaging/ILineMessagingClient.cs:25`), but F05A stores and accepts the
concrete `LineMessagingClient`
(`LineMessagingProcessorClass.cs:32,54-56`).

Consequences:

- F05A tests require a concrete SDK client and capturing `HttpMessageHandler`;
- F06 directly injects concrete F05A
  (`LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:25-29`,
  `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:27-31`);
- F07 defines a separate processor interface and 15-method pass-through adapter
  (`LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:22-97`,
  `LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs:22-91`);
- F05B registers only the concrete class
  (`LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:62-65`).

This proves consumers are compensating for the missing F05A contract rather
than consuming a stable processor interface.

## Input And Output Contracts

Active F05A inputs are primitive strings, SDK message lists, SDK RichMenu types,
and streams. Outputs are `Task`, SDK response types, or the duplicate legacy
`UserProfile`.

F05A does not own a typed processor result. F06 and F07 already own richer
workflow results:

- notification result:
  `LineMessagingProcessor.Workflows/LineNotificationResult.cs:19-74`;
- reply result:
  `LineMessagingProcessor.Workflows/LineReplyResult.cs:21-58`;
- RichMenu result:
  `LineMessagingProcessor.RichMenus/LineRichMenuResult.cs:20-113`.

The correct extraction is not to duplicate those result models in F05A.
F05A should expose transport capability interfaces with explicit cancellation
and ownership, while F06/F07 retain business/workflow classification.

## Credential And Lifetime Contract

Four constructors express incompatible behavior:

- hidden process-global ambient configuration;
- token-owned concrete SDK client;
- externally owned concrete SDK client;
- caller-supplied configuration that still creates an owned SDK client.

There is no ownership marker. `Dispose` does not implement either contract
(`LineMessagingProcessorClass.cs:132-155`).

Credential validation is method-specific rather than construction-specific
(`LineMessagingProcessorClass.cs:120-129,275-278,293-329`).

Clean contract:

- composition validates configuration and constructs/injects the client;
- F05A accepts an interface, not configuration;
- ownership is explicit and normally external;
- compatibility factories may own a client but must dispose it;
- all operations share one fail-fast credential policy.

## Event And Handler Contract

The dynamic dispatcher accepts `dynamic`, mutates public fields, performs
string-switch dispatch, parses postback data by fixed positions, and returns no
typed result (`LineMessagingProcessorClass.cs:158-253,699-711`).

No current caller was found, so it should not shape the clean core interface.
If compatibility requires it:

- F05B owns authenticated webhook envelope verification;
- a dedicated legacy event adapter converts `unknown`/dynamic input to typed
  F04 webhook events;
- dispatch returns a typed handled/ignored/failure result;
- no mutable instance fields carry event state;
- product messages and binding behavior remain B07-owned.

## Recommended Capability Contracts

Suggested responsibility shape, without prescribing implementation names:

1. Message transport:
   - input: recipient ID, typed SDK messages, optional retry key, cancellation;
   - operation: push/reply;
   - output: completion or provider exception consumed by F06.
2. Profile transport:
   - input: user/group/room identifiers, cancellation;
   - output: F04 profile DTO;
   - no duplicate F05A profile DTO.
3. RichMenu transport:
   - input/output: F04 RichMenu provider types, stream, cancellation;
   - consumed directly by F07, replacing its pass-through adapter.
4. Legacy compatibility adapter:
   - magic confirmation text, ambient configuration, binding helper, dynamic
     event parsing, old DTO;
   - obsolete, separately testable, no longer the clean dependency.

## Test Seam

Current tests prove HTTP endpoint/payload behavior by constructing:

```text
capturing handler -> HttpClient -> concrete F04 client -> concrete F05A
```

This is useful provider-contract coverage but not a processor unit seam.

Required extraction tests:

- fake F04 interface verifies F05A validation/delegation;
- fake F05A capability verifies F06/F07 behavior without HTTP;
- ownership fake verifies disposal;
- cancellation fake verifies token propagation;
- compatibility tests preserve magic legacy behavior only in the legacy
  adapter;
- event adapter tests cover malformed/null/unsupported input without side
  effects.

## Counter-Evidence

- F04 already owns serialization, HTTP, and SDK model validation; do not
  duplicate it in F05A.
- F06/F07 already own typed workflow results; do not move them into F05A.
- F05B already centralizes the preferred `IHttpClientFactory` composition.
- Current DI lifetime is transient, so public mutable fields were not promoted
  as a proven singleton cross-request defect.
- Current active F05A pass-through methods are asynchronous and do not block.

## Ownership Handoffs

1. F04: interface/cancellation-capable HTTP operations.
2. F05B: option validation, DI registration, concrete lifetime.
3. F06: notification/reply validation and result classification.
4. F07: catalog, state, provisioning, assignment, and RichMenu results.
5. B07: webhook/product event semantics, binding URL/message, profile use.
6. B05: payment notification composition migration.
7. X02B: internal logging and correlation.
8. F01A: duplicate project lifecycle.
