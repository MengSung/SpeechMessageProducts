# F05A Scope Manifest

Status: COMPLETE
Mode: STATIC_READ_ONLY
Leaf: F05A LINE Processor Core

## Authoritative Boundary

Primary owner:

- `LineMessagingProcessor/**`
- core tests under `LineMessagingProcessor.Tests/**`
- `Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs`
- processor API and compatibility behavior

Explicit exclusions:

- `LineMessagingProcessor.AspNetCore/**` and tests: F05B
- `LineMessagingProcessor.Workflows/**` and tests: F06
- `LineMessagingProcessor.RichMenus/**` and tests: F07
- ChurchReport LINE/payment/business integration: B05/B07 and other business
  leaves
- `Line.Messaging/**` implementation and tests other than the explicit subject
  test: F04

Excluded files were read only when needed to prove dependency, consumer,
lifetime, cancellation, or result-contract behavior.

## Owned File Inventory

| Path | Lines | SHA-256 | Role |
|---|---:|---|---|
| `LineMessagingProcessor/LineMessagingProcessorClass.cs` | 730 | `A2AE3D066DABB64084DDBC5E189C34B1F44169E0DE83962710C031F0C5870CFC` | Sole processor implementation, compatibility surface, DTO |
| `LineMessagingProcessor/LineMessagingProcessor.csproj` | 50 | `9D97922E9391A048B928635439559727C255121EC6ECF517446F7385981AE23E` | Canonical project |
| `LineMessagingProcessor/LineMessagingProcessor_Net10.csproj` | 28 | `26F9FC4DCFD7746FC10ABE3EBB8638DF2F0D3D0ABAFF74A0DA95B471E81AF527` | Incomplete duplicate/historical project |
| `LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj` | 26 | `0C7CCE5203D9AF61BF399B574640C3F719B924233B393010260A9DBE7B90D974` | Core test container |
| `LineMessagingProcessor.Tests/LineMessagingProcessorSendMessageTests.cs` | 100 | `7D3350BE4A0B7354E4A0C4DE76B20164FB9C1D3507E8C0500FA00D5F01EDF0F8` | Legacy text send/validation |
| `LineMessagingProcessor.Tests/LineMessagingProcessorReliableNotificationTests.cs` | 98 | `21E0B7F60C27FA1923E936D5DA5DF893ED590FBF987ACE92A94F56A9BC201A8F` | Retry-key send |
| `LineMessagingProcessor.Tests/LineMessagingProcessorIdentityProfileTests.cs` | 95 | `F0DDD4CBB2337D87CAE1ED2CBCCDD77C7A49741EE52C9CE4F422BA930C841097` | User profile and legacy DTO |
| `LineMessagingProcessor.Tests/LineMessagingProcessorGroupRoomProfileTests.cs` | 128 | `49627A216B27316EE3C3374CF72F0BFF00CFCE720D37BF7B2E0F37CF0F3134C8` | Group/room profiles |
| `Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs` | 102 | `9008E59909B1AF3350D6C642E9F55F0D128AB05310C49BAEBF851B9BC9C77963` | Explicit F05A credential subject tests |

## Project Contract

Canonical project:

- targets `net10.0`
  (`LineMessagingProcessor/LineMessagingProcessor.csproj:4`);
- references F04
  (`LineMessagingProcessor/LineMessagingProcessor.csproj:47`);
- directly references configuration JSON/environment packages
  (`LineMessagingProcessor/LineMessagingProcessor.csproj:38-41`);
- directly references Newtonsoft and RestSharp
  (`LineMessagingProcessor/LineMessagingProcessor.csproj:42-43`).

The alternate project also targets `net10.0` but lacks the F04 project
reference and configuration packages
(`LineMessagingProcessor/LineMessagingProcessor_Net10.csproj:4-16`).
The authoritative map already classifies it as incomplete/historical. Runtime
diagnosis uses the canonical project; lifecycle decision is handed to F01A.

## Public Surface Map

Construction/configuration:

- default ambient configuration:
  `LineMessagingProcessorClass.cs:35,40-43,90-99`;
- token constructor:
  `LineMessagingProcessorClass.cs:45-52`;
- concrete SDK constructor:
  `LineMessagingProcessorClass.cs:54-59`;
- injected `IConfiguration` constructor:
  `LineMessagingProcessorClass.cs:61-64`.

Legacy event/shared state:

- public fields:
  `LineMessagingProcessorClass.cs:37-38`;
- dynamic dispatcher:
  `LineMessagingProcessorClass.cs:158-253`;
- postback parser:
  `LineMessagingProcessorClass.cs:699-711`.

Transport families:

- push:
  `LineMessagingProcessorClass.cs:255-330`;
- reply:
  `LineMessagingProcessorClass.cs:339-352`;
- RichMenu:
  `LineMessagingProcessorClass.cs:361-563`;
- profile:
  `LineMessagingProcessorClass.cs:575-653`.

Product/compatibility helpers:

- display-name/error helper:
  `LineMessagingProcessorClass.cs:655-671`;
- binding URL/message helper:
  `LineMessagingProcessorClass.cs:673-696`;
- duplicate compatibility DTO:
  `LineMessagingProcessorClass.cs:716-729`.

## Dependency Evidence

F04 provides:

- `ILineMessagingClient`
  (`Line.Messaging/ILineMessagingClient.cs:25`);
- concrete externally-owned `HttpClient` construction
  (`Line.Messaging/LineMessagingClient.cs:107-115`);
- obsolete internally-owned `HttpClient` construction
  (`Line.Messaging/LineMessagingClient.cs:117-131`);
- provider serialization/HTTP and disposal.

F05A currently depends on concrete `LineMessagingClient`, not the F04
interface (`LineMessagingProcessorClass.cs:32,54-56`).

## Consumer Evidence

F05B composition:

- creates F04 through `IHttpClientFactory`
  (`LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:54-60`);
- registers concrete F05A transient
  (`LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:62-65`).

F06:

- notification and reply workflows hold concrete F05A
  (`LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:25-29`,
  `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:27-31`);
- F06 owns typed workflow results and exception classification
  (`LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:40-82`,
  `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:42-85`).

F07:

- defines its own 15-method processor interface
  (`LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:22-97`);
- adapts concrete F05A through pass-through methods
  (`LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs:22-91`).

B05/B07:

- token-created workflows:
  `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:136-165`,
  `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:310-337`;
- a controller uses `using` around token-created F05A
  (`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:607-619`);
- profile provider accepts cancellation but cannot propagate it
  (`SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs:56-65`);
- multiple legacy tools construct concrete F05A around an existing concrete F04
  client (`SpeechMessageProducts.ChurchReport/Tools/ReplyUtility.cs:37-82`,
  `SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs:201-204,274-300`).

## Test Coverage And Gaps

Covered:

- normal and magic legacy text send;
- retry-key header;
- blank user/message guards;
- user/group/room profile endpoints;
- legacy profile DTO mapping;
- token/config precedence and one missing-token path.

Not covered:

- processor disposal or SDK ownership;
- finalizer behavior;
- cancellation propagation;
- `SendMessagesAsync`/`ReplyMessagesAsync` missing-token behavior;
- RichMenu pass-throughs in the F05A test project;
- dynamic event dispatcher, postback parser, or mutable-state concurrency;
- exception-to-recipient helpers;
- interface/fake processor seam.

## Gate State

The authoritative map classifies F05A as having a dedicated test candidate but
requiring a green baseline before optimization. This diagnostic did not run
restore/build/test and therefore records:

- diagnosis gate: ready;
- optimization gate: baseline not established in this run;
- runtime validation: deferred.

## Read-Only Statement

No product source, test, project, configuration, solution, workflow, map, or
task file was modified. No restore/build/test/package/generation/format/
migration command was run.
