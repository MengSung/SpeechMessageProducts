# F05B Scope Manifest

Status: COMPLETE
Mode: STATIC_READ_ONLY
Leaf: F05B LINE ASP.NET Core Composition Adapter

## Authoritative Boundary

Primary owner:

- `LineMessagingProcessor.AspNetCore/**`
- `LineMessagingProcessor.AspNetCore.Tests/**`
- ASP.NET Core service-registration contract
- options validation, DI lifetimes, adapter composition, and host-facing seams

Explicit exclusions:

- `LineMessagingProcessor/**`: F05A processor core
- `LineMessagingProcessor.Workflows/**`: F06 workflow logic
- `LineMessagingProcessor.RichMenus/**`: F07 RichMenu logic and state behavior
- `Line.Messaging/**`: F04 SDK and HTTP implementation
- `SpeechMessageProducts.ChurchReport/**`: X01/B07/B05 host and product logic

Excluded files were read only to prove dependency construction, consumer
resolution, lifetime fan-out, endpoint trust, or ownership handoffs.

## Owned File Inventory

| Path | Lines | SHA-256 | Role |
|---|---:|---|---|
| `LineMessagingProcessor.AspNetCore/LineMessagingProcessorOptions.cs` | 24 | `98300E369F93DF88E2314572C3047FD1497F8DF6BC7F2E8F56DDAE2244F3F5A6` | Token and API endpoint options |
| `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs` | 136 | `E5E457AC2D431DADA9569626196AB49BCC6641D3BB2115DF620F32436875C5AA` | DI registration and composition bundle |
| `LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj` | 19 | `8299371CD6B02BDECC1ECE1C961D008A0ED9279AEFD3986AB437064076E08672` | Adapter project graph |
| `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs` | 199 | `1BF25861B80C93078A075371B1D19AC17C152AFAB555A10D3DEFE65C74D19D78` | Registration and RichMenu composition tests |
| `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj` | 26 | `F59A1DA8DC7D8C36BB4EF2C4BA91409F2CB33A61E4D0EABF3196D25AF187ECFB` | Subject test project |

## Public Registration Surface

Options:

- `ChannelAccessToken` defaults to empty
  (`LineMessagingProcessorOptions.cs:21`);
- `ApiBaseUri` defaults to LINE API v2
  (`LineMessagingProcessorOptions.cs:23`).

Core registration:

- options action via `services.Configure`
  (`LineMessagingProcessorServiceCollectionExtensions.cs:53`);
- named `HttpClientFactory` registration
  (`LineMessagingProcessorServiceCollectionExtensions.cs:54`);
- transient F04 client factory
  (`LineMessagingProcessorServiceCollectionExtensions.cs:55-61`);
- transient concrete F05A processor
  (`LineMessagingProcessorServiceCollectionExtensions.cs:62-63`);
- transient F06 notification and reply workflows
  (`LineMessagingProcessorServiceCollectionExtensions.cs:64-65`);
- implicit F07 registration
  (`LineMessagingProcessorServiceCollectionExtensions.cs:68`).

RichMenu registration:

- optional trigger-options replacement
  (`LineMessagingProcessorServiceCollectionExtensions.cs:88-97`);
- singleton ID cache and state store
  (`LineMessagingProcessorServiceCollectionExtensions.cs:100-101`);
- transient processor/workflow/policy/orchestrator services
  (`LineMessagingProcessorServiceCollectionExtensions.cs:102-111`);
- product catalog/provisioning extension
  (`LineMessagingProcessorServiceCollectionExtensions.cs:121-133`).

## Dependency Evidence

F04:

- F05B constructs concrete `LineMessagingClient`, not
  `ILineMessagingClient`;
- F04 sets the default Authorization header and normalizes the supplied URI
  (`Line.Messaging/LineMessagingClient.cs:107-115,134-155`).

F05A:

- F05B constructs concrete `LineMessagingProcessorClass`;
- F05A retains public mutable fields and a finalizer, so process-wide singleton
  reuse is not currently safe
  (`LineMessagingProcessor/LineMessagingProcessorClass.cs:27-38,132-155`).

F06:

- notification and reply workflow constructors require concrete F05A
  (`LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs:25-30`,
  `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs:27-32`).

F07:

- `LineMessagingProcessorRichMenuAdapter` requires concrete F05A
  (`LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs:22-35`);
- the singleton ID cache locks its state
  (`InMemoryLineRichMenuIdCache.cs:25-31,43-52,70-77`);
- the singleton user-state store uses `ConcurrentDictionary` keyed by LINE user
  ID (`InMemoryRichMenuStateStore.cs:23-35`).

## Consumer Evidence

X01 host composition:

- ChurchReport calls `AddLineMessagingProcessor` once and falls back to an
  empty token (`SpeechMessageProducts.ChurchReport/Startup.cs:503-510`);
- it then calls product RichMenu provisioning
  (`SpeechMessageProducts.ChurchReport/Startup.cs:511-515`).

Multi-capability resolution:

- BaseChurchController resolves notification and reply independently in one
  request scope (`BaseChurchController.cs:276-283`);
- ContextDictionary repeats the same pair
  (`ContextDictionary.cs:98-106`);
- ChurchReport binding composition uses a concrete-processor profile provider
  plus an independently resolved notification workflow
  (`ChurchReportLineBindingNotificationService.cs:46-52,83-91`).

## Test Coverage And Gaps

Covered:

- basic concrete client/processor/workflow resolution;
- RichMenu trigger behavior after a second configuration call;
- `ValidateOnBuild` and `ValidateScopes` for the happy path;
- product catalog/provisioning resolution;
- manual fake RichMenu processor replacement using `RemoveAll`.

Not covered:

- blank token or invalid endpoint startup failure;
- HTTP versus HTTPS endpoint policy;
- Authorization destination;
- descriptor lifetimes and identity within/across scopes;
- number of clients/processors created for multiple capabilities;
- disposal at scope end;
- repeated `AddLineMessagingProcessor` invocation;
- custom pre-registration override;
- `IEnumerable<T>` duplicate cardinality;
- additive versus replacing RichMenu trigger configuration;
- independent F06/F07 opt-in;
- named/typed client seam.

## Gate State

The authoritative map requires provider and consumer baselines before
optimization. This diagnosis did not run restore/build/test and records:

- diagnosis gate: ready;
- optimization gate: baseline not established in this run;
- runtime validation: deferred;
- consumer gates: F04/F05A/F06/F07 subject tests plus X01 DI resolution/startup.

## Read-Only Statement

No product source, test, project, configuration, solution, workflow, map, task,
or other workspace file was modified. No restore/build/test/package/generation/
format/migration/benchmark command was run.
