# F03B Scope Manifest

Status: COMPLETE
Module: F03B
Workspace: F03B-toolutility-line-adapter
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED

## Authoritative Ownership

The authoritative map assigns F03B:

- `ToolUtility/LineMessaging/ILineMessageService.cs`
- `ToolUtility/LineMessaging/LineMessageService.cs`
- `ToolUtility/PushUtility.cs`
- `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs`
- `ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs`
- the `ToolUtility/ToolUtility.csproj:52` reference to
  `Line.Messaging/Line.Messaging.csproj` as an F03B build requirement

Map evidence:

- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:137`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:177`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:181`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:187`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:697`

## Primary Responsibilities Found

1. `PushUtility` accepts a concrete F04 `LineMessagingClient` and sends push or
   multicast payloads (`ToolUtility/PushUtility.cs:29`,
   `ToolUtility/PushUtility.cs:34`, `ToolUtility/PushUtility.cs:44`,
   `ToolUtility/PushUtility.cs:89`).
2. The same class invokes CRM-backed audit persistence before selected sends
   (`ToolUtility/PushUtility.cs:58`, `ToolUtility/PushUtility.cs:82`).
3. `ToolUtilityClass.Line` resolves each recipient to a CRM contact and creates
   a `letter` record containing subject, full message, and LINE ID
   (`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:33`,
   `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:40`,
   `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:42`,
   `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:43`,
   `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:58`).
4. `ILineMessageService`/`LineMessageService` define a separate CRM-only
   persistence surface which creates a `linemessage` entity and performs no LINE
   transport (`ToolUtility/LineMessaging/ILineMessageService.cs:18`,
   `ToolUtility/LineMessaging/LineMessageService.cs:31`,
   `ToolUtility/LineMessaging/LineMessageService.cs:34`,
   `ToolUtility/LineMessaging/LineMessageService.cs:41`).
5. Legacy RichMenu creation/deletion and sample message composition remain
   public in `PushUtility` (`ToolUtility/PushUtility.cs:301`,
   `ToolUtility/PushUtility.cs:370`, `ToolUtility/PushUtility.cs:415`,
   `ToolUtility/PushUtility.cs:436`, `ToolUtility/PushUtility.cs:493`).

## Dependencies Read Only

- F04 `Line.Messaging` owns HTTP, JSON serialization, retry headers, and SDK
  client disposal. The injected-`HttpClient` constructor does not own the
  client, while the token-only constructor creates and owns one
  (`Line.Messaging/LineMessagingClient.cs:60`,
  `Line.Messaging/LineMessagingClient.cs:107`,
  `Line.Messaging/LineMessagingClient.cs:110`,
  `Line.Messaging/LineMessagingClient.cs:124`,
  `Line.Messaging/LineMessagingClient.cs:126`,
  `Line.Messaging/LineMessagingClient.cs:2823`).
- F03A provides CRM contact lookup, entity creation, and the singleton factory.
  F03B reads these capabilities but does not own their implementation
  (`ToolUtility/Factory/ToolUtilityFactory.cs:76`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:84`,
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:85`).
- F03Q owns `ToolUtility/Core/ToolUtilityFacade.cs`; it is read only. It wires
  `ILineMessageService` and exposes the alternate `CreatePushLineMessage`
  persistence path (`ToolUtility/Core/ToolUtilityFacade.cs:64`,
  `ToolUtility/Core/ToolUtilityFacade.cs:146`,
  `ToolUtility/Core/ToolUtilityFacade.cs:527`).

## Consumers Read Only

The only explicit ChurchReport alias to the F03B `ToolUtility.PushUtility` is:

- `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:34`

That consumer:

- creates a token-only `LineMessagingClient` and an F03B `PushUtility`
  (`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:61`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:65`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:67`);
- calls `MultiCastTextMessageAsync` without awaiting or observing the returned
  task
  (`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:111`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:129`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:146`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:163`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:195`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:226`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:477`);
- supplies member names and weekly-report content as message data
  (`SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:186`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:191`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:195`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:463`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:473`,
  `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:477`).

These ChurchReport files are B07/B03 dependencies only and were not modified.

## Explicit Exclusions

- F03A CRM operation implementation outside the F03B exception files.
- F03Q `ToolUtility/Core/ToolUtilityFacade.cs`.
- ChurchReport LINE workflows, `SpeechMessageProducts.ChurchReport/Tools/PushUtility.cs`,
  `ReplyUtility`, `LineUtilityClass`, payment and QR business flows.
- F04 SDK internals and tests.
- All product, project, solution, configuration, task, map, and workflow writes.

## Subject Tests And Gate

The only F03B-owned test file is
`ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs`. It does not cover
`PushUtility`, multicast, CRM audit ordering, failure policy, client ownership,
or RichMenu lifecycle. Its constructor arrangement is also inconsistent:

- production requires `IOrganizationService`
  (`ToolUtility/LineMessaging/LineMessageService.cs:25`);
- the test creates `Mock<IEntityCrudService>` and passes that object
  (`ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs:30`,
  `ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs:33`).

The executable gate is independently blocked because `ToolUtility` targets
`net10.0`, `ToolUtility.Tests` targets `net8.0`, and the test project is absent
from the solution (`ToolUtility/ToolUtility.csproj:4`,
`ToolUtility.Tests/ToolUtility.Tests.csproj:4`,
`SpeechMessageProducts.sln:14`). F01A/F01D own container enrollment and target
repair; F03B owns correction and expansion of its subject tests after that gate
exists.

## Scope Conclusion

F03B is a thin physical exception inside a CRM project, but its current public
surface is not a clean LINE adapter. It combines F04 transport, F03A/F03Q CRM
state, inconsistent failure semantics, and legacy RichMenu operations. All
diagnostic conclusions below keep those ownership boundaries explicit.
