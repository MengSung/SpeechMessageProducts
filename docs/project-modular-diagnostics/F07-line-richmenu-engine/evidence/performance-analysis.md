# F07 Performance Analysis

Status: DEGRADED_CCG_REVIEW_APPLIED
Nested agent count: 0

## Confirmed Performance Findings

### F07-PERF-001: Provider calls cannot be cancelled through the F07 processor contract

Evidence:

- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:27` defines `CreateRichMenuAsync(RichMenu richMenu)` with no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:32` defines upload with no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:37` defines list with no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:62` defines link with no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:67` defines unlink with no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:72` defines delete with no cancellation token.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:136` maps cancellation-aware assignment to a provider action without passing the token.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:262` maps cache-miss resolution to a provider query without passing the token.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:106` checks cancellation before each definition.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:194`, `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:199`, `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:203`, and `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:209` perform provider calls without a token.
- The read-only adapter boundary confirms the same shape in `LineMessagingProcessor/LineMessagingProcessorClass.cs:361`, `LineMessagingProcessor/LineMessagingProcessorClass.cs:375`, `LineMessagingProcessor/LineMessagingProcessorClass.cs:393`, `LineMessagingProcessor/LineMessagingProcessorClass.cs:424`, and `LineMessagingProcessor/LineMessagingProcessorClass.cs:452`.

Impact:

Hosted shutdown, request abort, or operator cancellation can stop local loops and stream copies but not provider calls already in progress. Provisioning and assignment can hold resources longer than expected and can continue mutating provider state after the caller has cancelled.

### F07-PERF-002: Cache-miss assignment performs full image materialization and a full provider list call per missing menu key

Evidence:

- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:240` loads catalog definitions.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:248` opens the PNG stream to resolve one menu key.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:254` materializes the image bytes.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:255` computes the expected provider name.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:262` calls `GetRichMenuListAsync`.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:269` holds all returned menus in memory.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:270` searches the whole provider list.
- `LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs:104` asserts the cache-miss path calls `list`.

Issue mapping:

- Retained as `F07-007` in `issue.md`.

Impact:

Cold cache assignment is expensive and repeats per process/cache miss. In large channels or after app restart, assignment hot paths can perform repeated PNG reads plus provider list calls instead of relying on provisioning output or a durable menu id store. This also amplifies LINE API latency and rate-limit exposure.

### F07-PERF-003: Default in-memory user state is unbounded and expiry sweep is O(n)

Evidence:

- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:29` stores all user states in a `ConcurrentDictionary`.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:46` inserts or replaces states without capacity, TTL eviction, or size policy.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:65` enumerates all state values.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:67` materializes expired records with `ToList()`.
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:101` registers the in-memory store as a singleton default.
- `LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs:67` processes expired records serially.

Impact:

State size grows with distinct users assigned through F07 in a process lifetime. Sweep cost scales with all stored users, not only expired users. This is acceptable for small deployments but is a performance and memory risk for high-volume LINE channels unless a bounded/durable `IRichMenuStateStore` replaces the default.

## Bounded Performance Observation

### F07-PERF-004: Copy-on-write menu id cache copies the full dictionary on every update

Evidence:

- `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:31` stores a dictionary snapshot.
- `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:70` enters the update lock.
- `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:72` copies the full dictionary during `Set`.
- `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:96` copies the full dictionary during `Remove`.
- `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:107` copies the full dictionary during `Snapshot`.
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:100` registers it as a singleton default.

Retention decision:

- Not retained as a top-level issue because catalog/menu-key cardinality is normally small and bounded.
- Recorded in `issue.md` non-retained observations for traceability.

Impact:

Menu key count is usually small and bounded by catalog size, so this is lower risk than user state growth. It becomes relevant if catalog provisioning is repeatedly invoked with many generated menu keys or if consumers use `SetSnapshot`/`Snapshot` in hot paths.

## Bounded Areas

- No sync-over-async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`) was found in F07 production source.
- No timers or event subscriptions were found in F07 production source; disposal leakage risk is limited to streams, and provisioning/assignment generally disposes streams with `await using` or `using`.
- Provisioning sync is intentionally serial; no batching or parallel provider operations were found. This avoids concurrent provider mutations but increases end-to-end provisioning time.
