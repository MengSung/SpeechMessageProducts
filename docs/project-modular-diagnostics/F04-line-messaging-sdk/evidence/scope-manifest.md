# F04 Scope Manifest

Status: COMPLETE
Module: F04
Workspace: F04-line-messaging-sdk
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED

## Authoritative Ownership

The module map assigns F04:

- `Line.Messaging/**`
- LINE API request/response and webhook models
- JSON serialization and converters
- HTTP/client/stream ownership
- provider error and retry contracts
- `Line.Messaging/Line.Messaging.csproj` as canonical project
- `Line.Messaging/Line.Messaging_Net10.csproj` as duplicate/historical project
- F04-owned tests under `Line.Messaging.Tests/**`

Map evidence:

- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:96`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:97`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:98`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:139`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:828`

## Primary Responsibilities Found

1. `LineMessagingClient` is a 2,832-line concrete client implementing a
   921-line `ILineMessagingClient` across messaging, content, profiles,
   webhooks, RichMenu, insights, coupons, membership, and placeholder audience
   APIs (`Line.Messaging/LineMessagingClient.cs:60`,
   `Line.Messaging/ILineMessagingClient.cs:23`).
2. `LiffClient` separately owns LIFF HTTP calls and repeats token/client/error
   patterns (`Line.Messaging/Liff/LiffClient.cs:27`).
3. `WebhookRequestMessageHelper`, `WebhookEventParser`, `WebhookEvent`, and
   event/message subclasses own webhook signature verification and dynamic
   projection (`Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:26`,
   `Line.Messaging/Webhooks/WebhookEventParser.cs:19`,
   `Line.Messaging/Webhooks/WebhookEvent.cs:23`).
4. `CamelCaseJsonSerializerSettings` and custom converters define outbound JSON
   casing, enum strings, and null omission
   (`Line.Messaging/Json/CamelCaseJsonSerializerSettings.cs:20-27`).
5. `HttpResponseMessageExtensions` and exception DTOs own provider error
   mapping (`Line.Messaging/HttpResponseMessageExtensions.cs:20`,
   `Line.Messaging/Exceptions/LineResponseException.cs:22`).
6. `ContentStream` wraps media streams and copied response content headers
   (`Line.Messaging/ContentStream.cs:28`, `:54`, `:62`).

## Canonical And Historical Project Definitions

- Both projects target `net10.0` and contain the same package/version/settings
  (`Line.Messaging/Line.Messaging.csproj:4-54`,
  `Line.Messaging/Line.Messaging_Net10.csproj:4-54`).
- A byte diff found only a UTF-8 BOM on the canonical file.
- The solution and every repository `ProjectReference` found use
  `Line.Messaging.csproj`; no active source/build reference to
  `_Net10.csproj` was found.
- Historical upgrade documents/scripts still mention `_Net10.csproj`, so
  retirement requires an F01A lifecycle decision rather than silent deletion.

## Subject Tests

F04-owned subject tests:

- `Line.Messaging.Tests/LineMessageModelTests.cs`
- `Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs`
- `Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs`

The fourth file,
`Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs`, tests F05A
processor token/configuration behavior and should follow the subject to F05A.
That file forces the F04 test project to reference
`LineMessagingProcessor/LineMessagingProcessor.csproj`
(`Line.Messaging.Tests/Line.Messaging.Tests.csproj:24`).

Coverage present:

- selected endpoint URL/host construction;
- selected retry-key headers;
- two new message-model validation rules.

Coverage absent:

- shared-client token isolation;
- webhook signature/parser fixtures;
- redelivery/event identity;
- error matrix and LIFF failure mapping;
- 409 accepted retry behavior and retry-key format;
- cancellation;
- streaming/response ownership;
- all public model/API parity.

## Dependencies Read Only

- External LINE Messaging/LIFF APIs and webhook protocol.
- `Newtonsoft.Json` 13.0.3
  (`Line.Messaging/Line.Messaging.csproj:53`).
- .NET `HttpClient`, `HttpRequestMessage`, `HttpResponseMessage`, streams,
  headers, and cryptography.

No external network request was made. Current official protocol references were
consulted only as documentation:

- `https://developers.line.biz/en/reference/messaging-api/`
- `https://learn.microsoft.com/dotnet/api/system.net.http.httpcompletionoption`

## Consumers Read Only

Direct project consumers include:

- F03B `ToolUtility`
- F05A `LineMessagingProcessor`
- F05B ASP.NET Core composition
- F06 notification/reply workflows
- F07 RichMenu workflows
- ChurchReport host and tests

Representative evidence:

- `ToolUtility/ToolUtility.csproj:52`
- `LineMessagingProcessor/LineMessagingProcessor.csproj:47`
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj:14`
- `LineMessagingProcessor.Workflows/LineMessagingProcessor.Workflows.csproj:11`
- `LineMessagingProcessor.RichMenus/LineMessagingProcessor.RichMenus.csproj:11`
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:54-60`

Consumers were read only to verify contract use. Recipient selection, product
binding, workflow policy, and RichMenu business behavior remain outside F04.

## Explicit Exclusions

- Recipient/user/group/room business decisions.
- ChurchReport LINE binding, profile lookup, notification content, CRM, payment,
  QR, or controller behavior.
- F03B adapter implementation.
- F05A processor internals except read-only contract consumption.
- F05B DI implementation except current client-lifetime counter-evidence.
- F06 notification/reply policy and F07 RichMenu business logic.
- Product source, projects, solution, tests, maps, workflows, and task writes.

## Gate Conclusion

The map requires the F04 provider gate plus F03B, F05A-F07, F05B, and host
consumer gates. This run intentionally did not execute them. Optimization
cannot be declared until the duplicate project and cross-owned test dependency
are resolved and executable gates are explicitly authorized.
