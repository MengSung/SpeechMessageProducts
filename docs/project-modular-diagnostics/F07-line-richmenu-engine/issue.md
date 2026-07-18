# F07 LINE RichMenu Engine Issues

Status: APPROVED_DEGRADED_WITH_WARNINGS
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Summary

F07 has a clean module boundary for catalog/provisioning/assignment/policy/state/cache, but the diagnosis found seven retained issues:

1. Temporary RichMenu TTL is modeled but not persisted, so expiry sweeps cannot restore/unassign those menus if a policy uses TTL.
2. Same-menu assignment trusts local state and skips LINE provider reconciliation.
3. Provisioning can reuse a provider menu created during a failed upload because reuse is name-only.
4. Provider calls cannot be cancelled through the F07 processor contract.
5. Default in-memory user state is unbounded and expiry sweep is O(n).
6. Public legacy workflow can delete the provider RichMenu currently linked to one user.
7. Cache-miss assignment performs image materialization and a provider list call per missing menu key.

## Confirmed Issues

### F07-001: RichMenu TTL is exposed by decisions but never persisted

Severity: Medium
Category: Security / stale assignment / expiry
Status: CONFIRMED_DEGRADED_CCG

Evidence:

- `LineMessagingProcessor.RichMenus/RichMenuDecision.cs:52` says TTL should be written into state for later sweep.
- `LineMessagingProcessor.RichMenus/RichMenuDecision.cs:56` exposes `Ttl`.
- `LineMessagingProcessor.RichMenus/RichMenuDecision.cs:75` accepts `ttl` in `Assign(...)`.
- `LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs:102` calls assignment with only `lineUserId`, `menuKey`, and `cancellationToken`.
- `LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs:28` has no TTL/expiry parameter.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:148` always stores `expiresAt: null`.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:66` only returns states with `ExpiresAt.HasValue`.

Impact:

A policy can request a temporary menu, but the assignment becomes non-expiring. The built-in text trigger policy currently does not pass TTL, so this is a latent contract defect until a custom or future policy uses TTL. Once used, users can stay on a privileged, campaign, onboarding, or workflow-specific RichMenu indefinitely unless another path explicitly changes it.

CCG round history:

- Round 1: Claude confirmed the finding but warned that the impact should mention no current built-in policy passes TTL; Gemini was quota-blocked. Applied by downgrading severity to Medium and clarifying latent-contract impact.

### F07-002: Same-menu assignment skips provider reconciliation based on local state

Severity: Medium
Category: Security / stale assignment / provider-state drift
Status: CONFIRMED_DEGRADED_CCG

Evidence:

- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:120` reads previous local state.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:121` checks whether `CurrentMenuKey` already matches.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:123` returns success with `changed: false`.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:136` is the provider call path that is skipped by the early return.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:137` links the provider when not skipped.
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:101` registers default in-memory state as singleton.

Impact:

If provider state changes outside F07, or a custom/durable state store is stale, F07 can claim the intended menu is linked without calling LINE. The local state cache becomes a source of truth for idempotency.

CCG round history:

- Round 1: Claude confirmed; Gemini was quota-blocked.

### F07-003: Provisioning can bind aliases/default/cache to a partially uploaded provider RichMenu

Severity: High
Category: Security / provisioning integrity / provider-state drift
Status: CONFIRMED_DEGRADED_CCG

Evidence:

- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:82` reads existing provider menus once.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:88` builds `existingByName`.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:173` treats a matching name as up-to-date.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:177` upserts alias on the reuse path.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:180` can set default on the reuse path.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:194` creates a provider RichMenu before image upload.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:199` uploads image after creation.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:126` catches per-definition exceptions.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:130` records a failed item without created-id cleanup/quarantine.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:138` reports no deleted provider ids.

Impact:

If create succeeds and upload fails, a later sync can see the versioned name and reuse the remote record without re-uploading image content. That can make aliases/default/cache point to a broken menu.

CCG round history:

- Round 1: Claude confirmed; Gemini was quota-blocked.

### F07-004: F07 cancellation tokens do not reach provider calls

Severity: Medium
Category: Performance / cancellation / lifecycle
Status: CONFIRMED_DEGRADED_CCG

Evidence:

- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:27` create has no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:32` upload has no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:37` list has no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:62` link has no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:67` unlink has no cancellation token.
- `LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs:72` delete has no cancellation token.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:136` calls provider link through a tokenless action.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:194` creates provider RichMenu without a token.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:199` uploads without a token.
- `LineMessagingProcessor/LineMessagingProcessorClass.cs:361` and `LineMessagingProcessor/LineMessagingProcessorClass.cs:393` confirm the read-only adapter boundary also lacks provider cancellation.

Impact:

Request aborts, host shutdown, and operator cancellation can stop local loops but cannot abort in-flight LINE API calls. Provider mutations can continue after cancellation.

CCG round history:

- Round 1: Claude confirmed; Gemini was quota-blocked. Claude noted F07 owns the F07 abstraction gap, with downstream F04/F05A support needed for full provider cancellation.

### F07-005: Default in-memory state is unbounded and expiry sweep scans all stored users

Severity: Medium
Category: Performance / memory / sweep hot path
Status: CONFIRMED_DEGRADED_CCG

Evidence:

- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:29` stores user states in a `ConcurrentDictionary`.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:46` inserts without a capacity or eviction policy.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:65` enumerates all values.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:67` materializes expired states with `ToList()`.
- `LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs:67` processes expired states serially.
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:101` registers the in-memory state store as singleton.

Impact:

Memory grows with distinct assigned users in the process lifetime, and sweep cost scales with all stored users rather than due records. This is a small-deployment default, not an optimized production state store.

CCG round history:

- Round 1: Claude confirmed; Gemini was quota-blocked.

### F07-006: Legacy delete workflow deletes provider RichMenus from a user-unassign style path

Severity: High
Category: Security / provider-side destructive operation
Status: CONFIRMED_DEGRADED_CCG

Evidence:

- `LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs:40` exposes `DeleteLinkedRichMenuAsync`.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:152` implements it.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:167` reads a user's current provider RichMenu id.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:168` unlinks the user.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:174` deletes the provider RichMenu id.
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:103` registers `ILineRichMenuWorkflow`.
- `LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs:83` through `LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs:86` assert the provider delete request.

Impact:

The currently linked provider RichMenu may be shared by other users, aliases, or channel defaults. A user-specific delete flow can remove shared provider state.

CCG round history:

- Round 1: Claude confirmed; Gemini was quota-blocked.

### F07-007: Cache-miss assignment performs image materialization and provider list lookup per missing menu key

Severity: Medium
Category: Performance / repeated provider calls / repeated materialization
Status: CONFIRMED_DEGRADED_CCG

Evidence:

- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:240` loads catalog definitions.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:248` opens the PNG stream to resolve one menu key.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:254` materializes the image bytes.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:255` computes the expected provider name.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:262` calls `GetRichMenuListAsync`.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:269` holds all returned menus in memory.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:270` searches the provider list.
- `LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs:104` asserts the cache-miss path calls `list`.

Impact:

Cold cache assignment is expensive and repeats per process/cache miss. After app restart or cache eviction, assignment hot paths can repeatedly read PNG content and call the provider list endpoint instead of using a durable provisioning index.

CCG round history:

- Round 1: Claude warned this was confirmed in `performance-analysis.md` but missing from retained `issue.md`; Gemini was quota-blocked. Applied by retaining it as F07-007.

## Non-Retained Observations

- F07 production source does not log tokens or user ids directly.
- F07 outbound action helper validates non-empty alias and postback data; inbound callback authorization is outside F07.
- `LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs:61` still searches for `ChurchReport.sln`; this is a validation risk after the solution rename, but not retained as a security/performance/extraction issue for F07.
- `F07-PERF-004` copy-on-write behavior in `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:72`, `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:96`, and `LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:107` is not retained as a top-level issue because menu-key cardinality is expected to be small and catalog-bounded.
