# F03B Extraction Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Current Boundary Is Not Cohesive

F03B exposes three different concepts under one ownership label:

1. LINE transport through a concrete F04 client
   (`ToolUtility/PushUtility.cs:29`, `ToolUtility/PushUtility.cs:44`,
   `ToolUtility/PushUtility.cs:89`).
2. CRM audit persistence through `ToolUtilityClass.Line`, which creates
   `letter` records (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:40`,
   `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:58`).
3. A second CRM-only `ILineMessageService`, which creates `linemessage` records
   and does not send LINE messages
   (`ToolUtility/LineMessaging/ILineMessageService.cs:20`,
   `ToolUtility/LineMessaging/LineMessageService.cs:31`,
   `ToolUtility/LineMessaging/LineMessageService.cs:34`,
   `ToolUtility/LineMessaging/LineMessageService.cs:41`).

The `ILineMessageService` name implies transport, but its only operation is
persistence. `PushUtility` does not depend on that interface; it reaches the
global `ToolUtilityClass` singleton directly
(`ToolUtility/PushUtility.cs:32`, `ToolUtility/PushUtility.cs:58`).

## Duplicate And Incompatible Audit Contracts

- F03B partial path: CRM entity `letter`, fields `subject`, `description`,
  `new_displayed_lineid`, contact regarding/from/to
  (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:40`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:41`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:42`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:43`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:45`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:55`).
- `LineMessageService` path: CRM entity `linemessage`, fields `userid`,
  `subject`, `message`
  (`ToolUtility/LineMessaging/LineMessageService.cs:34`,
  `ToolUtility/LineMessaging/LineMessageService.cs:36`,
  `ToolUtility/LineMessaging/LineMessageService.cs:37`,
  `ToolUtility/LineMessaging/LineMessageService.cs:38`).

F03Q wires the second service (`ToolUtility/Core/ToolUtilityFacade.cs:146`,
`ToolUtility/Core/ToolUtilityFacade.cs:527`), while F03B `PushUtility` uses the
first path. There is no single delivery/audit contract or status model.

## Failure Contract Is Inconsistent

- list and text overloads rethrow (`ToolUtility/PushUtility.cs:47`,
  `ToolUtility/PushUtility.cs:51`, `ToolUtility/PushUtility.cs:68`,
  `ToolUtility/PushUtility.cs:72`, `ToolUtility/PushUtility.cs:93`,
  `ToolUtility/PushUtility.cs:97`);
- image, video, audio, location, sticker, template, confirm, and imagemap
  overloads catch and suppress the exception
  (`ToolUtility/PushUtility.cs:115`, `ToolUtility/PushUtility.cs:119`,
  `ToolUtility/PushUtility.cs:135`, `ToolUtility/PushUtility.cs:139`,
  `ToolUtility/PushUtility.cs:230`, `ToolUtility/PushUtility.cs:234`,
  `ToolUtility/PushUtility.cs:282`, `ToolUtility/PushUtility.cs:286`);
- all methods return `Task` without a typed success/failure result, retry
  classification, provider request ID, or cancellation token.

This makes delivery requirements impossible to express at the adapter boundary.
The only explicit consumer compounds the problem by ignoring multicast tasks
(`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:111`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:129`,
`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:477`).

## Test Seam

The only F03B test verifies only that some entity creation occurs
(`ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs:28`,
`ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs:37`). It does not
assert entity fields or exercise `PushUtility`.

The test also passes `IEntityCrudService` to a constructor requiring
`IOrganizationService`
(`ToolUtility/LineMessaging/LineMessageService.cs:25`,
`ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs:30`,
`ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs:33`).
This is a source-level contract mismatch independent of the net8/net10
container blocker.

## Recommended Extraction Contract

F03B should become a narrow compatibility adapter, not another LINE workflow
engine:

- Input: typed recipient, typed message content, delivery importance,
  optional retry key, optional audit metadata, cancellation token.
- Output: typed delivery result containing success/failure classification and
  provider correlation data.
- Transport dependency: F04 `ILineMessagingClient` or the already reusable F06
  notification workflow, injected and externally lifetime-managed.
- Audit dependency: a separate F03A-owned interface accepting a minimized
  post-delivery audit record. Audit policy decides whether content is omitted,
  summarized, or retained.
- Compatibility: keep legacy `PushUtility` methods as adapters while consumers
  migrate; do not move ChurchReport recipient selection or business message
  composition into F03B.

## Ownership Handoffs

1. F03A: own CRM audit repository implementation and batching.
2. F03Q: remove mixed facade ownership of `ILineMessageService` after contract
   split.
3. F04: continue owning HTTP, serialization, SDK validation, and client
   disposal semantics.
4. F06: preferred reusable delivery workflow/result and retry semantics.
5. F07: own RichMenu provisioning/deletion lifecycle.
6. B07: migrate the sole explicit F03B consumer, await delivery, and use host
   DI-managed client/workflow.
7. F01A/F01D: establish an executable ToolUtility test container; F03B then
   repairs and expands its subject tests.

## Counter-Evidence

- F04 already contains a reusable interface and does not need to be duplicated
  (`Line.Messaging/ILineMessagingClient.cs:59`,
  `Line.Messaging/ILineMessagingClient.cs:92`).
- ChurchReport has newer workflow-backed LINE paths, so F03B should not absorb
  B07 workflows. Those files are excluded and remain read only.
- Repository search found no current caller of F03B `AddRichMenuMessage` or
  `DeleteRichMenuMessage`; they are retirement candidates, not justification
  for a new F03B RichMenu abstraction.
