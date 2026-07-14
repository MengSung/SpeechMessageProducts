# F05B Performance Analysis

Status: COMPLETE
Mode: STATIC_READ_ONLY

## Confirmed: Transient Capability Fan-Out Duplicates Equivalent Graphs

The owned registrations are all transient for transport and capability
services:

```text
LineMessagingClient
  -> LineMessagingProcessorClass
     -> ILineNotificationWorkflow
     -> ILineReplyWorkflow
     -> ILineRichMenuProcessor
        -> RichMenu workflows
```

Evidence:

- each client resolution calls `IHttpClientFactory.CreateClient`
  (`LineMessagingProcessorServiceCollectionExtensions.cs:55-60`);
- each processor resolution requests a new client
  (`LineMessagingProcessorServiceCollectionExtensions.cs:62-63`);
- each F06 workflow requests its own concrete processor
  (`LineNotificationWorkflow.cs:25-30`, `LineReplyWorkflow.cs:27-32`);
- the F07 adapter requests its own concrete processor
  (`LineMessagingProcessorRichMenuAdapter.cs:27-35`).

Current host paths independently resolve both notification and reply workflows
within one request (`BaseChurchController.cs:276-283`,
`ContextDictionary.cs:98-106`). The binding service composes a profile provider
backed by one concrete processor and a notification workflow backed by another
processor/client graph
(`ChurchReportLineBindingNotificationService.cs:46-52,83-91`).

Per graph, the static cost includes:

- one `HttpClient` wrapper from the factory;
- one Authorization default header assignment;
- one F04 serializer-settings object;
- one F05A processor/finalizable object;
- one workflow or adapter;
- DI disposable tracking until scope/provider disposal.

Handler pooling means this is not a socket-exhaustion issue. The retained
finding is duplicate wrappers and composition objects, F05B-PERF-001.

## HttpClient Ownership And Disposal

F04's externally supplied client constructor marks the client as non-owned
(`Line.Messaging/LineMessagingClient.cs:107-115`). F05B obtains each client from
`IHttpClientFactory`, and the DI container tracks factory-created disposable
transients for scope/provider cleanup.

Static conclusions:

- no internally created `HttpClient` exists in the F05B path;
- handler reuse is delegated to `IHttpClientFactory`;
- disposing the F04 wrapper does not dispose the factory client;
- the F05A processor has a finalizer, but DI disposal calls
  `GC.SuppressFinalize` when the processor is scope-tracked and disposed.

The exact allocation/finalizer impact needs runtime measurement. It does not
block the confirmed duplicate-graph diagnosis.

## Startup And Reflection Review

Owned registration-time work:

- add options configuration;
- add a named HttpClient;
- append service descriptors;
- one `services.Any` scan for trigger options;
- optional `RemoveAll`;
- construct one small trigger-options dictionary.

No owned source performs:

- reflection or assembly scanning;
- file or configuration-provider I/O;
- network calls;
- serializer warmup;
- service resolution during registration;
- `BuildServiceProvider`;
- blocking wait or sync-over-async.

Disposition: no meaningful startup/reflection issue retained. Repeated
registration causes extra descriptor/configuration entries under F05B-EXT-001,
but the static startup cost itself is minor.

## Singleton State Cost Review

F05B registers singleton RichMenu cache and state store
(`LineMessagingProcessorServiceCollectionExtensions.cs:100-101`).

Counter-evidence:

- ID cache access is locked and snapshots are copied;
- state store uses `ConcurrentDictionary`;
- entries are keyed by menu key or LINE user ID;
- workflows, policies, and adapters remain transient.

Potential unbounded retention and expiration sweep behavior belong to F07
because they are properties of the state-store/workflow contract. F05B should
retain an override seam for distributed or bounded stores, but no separate
F05B performance issue is promoted.

## Test Coverage Gaps

Subject tests resolve happy-path types and validate the service provider, but
do not assert:

- service descriptor lifetimes;
- same identity within one scope;
- different identity across scopes;
- number of client/processor factory invocations;
- disposal at scope end;
- repeated extension-call cardinality;
- allocation differences for multi-capability resolution.

## Runtime Hypotheses

1. Multi-capability request paths allocate two to four equivalent graphs.
2. Scoped sharing reduces allocations and DI disposal tracking without sharing
   F05A mutable state across requests.
3. F05A finalizer queue pressure is measurable when transient instances escape
   normal scope disposal.
4. Startup descriptor-scan time remains negligible relative to host startup.

No build, test, benchmark, restore, or runtime profiler was run.
