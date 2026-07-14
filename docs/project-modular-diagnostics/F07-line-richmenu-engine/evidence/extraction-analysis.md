# F07 Extraction Analysis

Status: DEGRADED_CCG_REVIEW_APPLIED
Nested agent count: 0

This analysis identifies extraction seams that would accelerate later optimization. These are based on module contracts and behavior boundaries, not file size.

## Existing Good Seams

- Catalog boundary: `ILineRichMenuCatalog` isolates product-owned menu definitions from F07 provisioning.
- Provider boundary: `ILineRichMenuProcessor` isolates F07 from the general LINE processor and SDK transport.
- Assignment boundary: `ILineRichMenuAssignmentWorkflow` isolates user-to-menu link/unlink behavior from orchestrator/policies.
- State boundary: `IRichMenuStateStore` isolates assignment/expiry state from the default in-memory implementation.
- Cache boundary: `ILineRichMenuIdCache` isolates menu key to provider id resolution.
- Policy boundary: `IRichMenuPolicy` and `IRichMenuOrchestrator` isolate decision logic from provider mutation.
- Trigger resolver boundary: `ILineRichMenuTextTriggerResolver` isolates text mapping from assignment.

## Recommended Extraction / Optimization Seams

### 1. Expiring assignment command contract

Problem addressed:

- F07-SEC-001: `RichMenuDecision.Ttl` is not carried into assignment state.

Recommended seam:

- Add an assignment request/command object, for example `RichMenuAssignmentRequest`, with `LineUserId`, `MenuKey`, `ExpiresAt` or `Ttl`, `Reason`, and optional idempotency/correlation metadata.
- Let `RichMenuOrchestrator` pass the selected decision into assignment instead of flattening it to `(lineUserId, menuKey)`.

Why this helps:

- Preserves the policy/assignment separation while making expiry an executable contract.
- Avoids adding many overloads to `ILineRichMenuAssignmentWorkflow`.

### 2. Provider menu resolver / provisioning index

Problem addressed:

- F07-PERF-002: cache-miss assignment recomputes fingerprints and calls provider list.

Recommended seam:

- Extract `ILineRichMenuResolver` or `IRichMenuProvisioningIndex` responsible for resolving `menuKey -> richMenuId`.
- Back it with provisioning output, durable cache, or a provider list snapshot, not per-assignment image reads.

Why this helps:

- Keeps assignment focused on link/unlink.
- Gives optimization work a single place for batching, memoization, and provider list invalidation.

### 3. Provisioning transaction / partial-failure tracker

Problem addressed:

- F07-SEC-003: create/upload/alias/default are not atomic and name-only reuse can hide partial upload failure.

Recommended seam:

- Extract a provisioning operation journal or `IRichMenuProvisioningState` with stages: `Created`, `ImageUploaded`, `AliasLinked`, `DefaultSet`, `Cached`.
- Record provider ids for failed stages and expose cleanup/retry decisions explicitly.

Why this helps:

- Enables safe cleanup or retry without expanding provisioning workflow responsibilities.
- Allows tests to verify partial failure behavior without real provider calls.

### 4. Provider API cancellation adapter

Problem addressed:

- F07-PERF-001: F07 cancellation tokens stop only local work.

Recommended seam:

- Add cancellation-aware methods to `ILineRichMenuProcessor`, or add a new `ILineRichMenuProviderClient` that carries `CancellationToken` on all provider operations.
- Keep the existing adapter as a compatibility layer until F04/F05A can accept cancellation.

Why this helps:

- Makes host shutdown/request abort semantics testable.
- Avoids scattering cancellation work across every workflow.

### 5. Durable and bounded state store

Problem addressed:

- F07-PERF-003 and stale assignment risks.

Recommended seam:

- Keep `IRichMenuStateStore`, but define production expectations: atomic compare/update, expiry indexes, bounded query for due records, and optional provider reconciliation marker.
- Move `InMemoryRichMenuStateStore` to an explicit development/testing default or document it as small-deployment only.

Why this helps:

- Does not force a specific backing store.
- Gives future Redis/DB implementations a clear behavioral contract.

### 6. Legacy destructive workflow isolation

Problem addressed:

- F07-SEC-004: `LineRichMenuWorkflow.DeleteLinkedRichMenuAsync` deletes provider resources.

Recommended seam:

- Split legacy create/delete provider-resource operations from user assignment operations.
- Do not register the destructive provider-resource workflow by default in normal RichMenu composition.
- If retained, gate it behind an explicit admin/provisioning interface name that makes provider deletion visible.

Why this helps:

- Reduces accidental provider-wide deletion from a user-unassign path.
- Keeps modern assignment semantics separate from legacy single-use RichMenu operations.

## Not Recommended as Extraction

- Do not extract `RichMenuActionFactory` just because it is separate; it is already a tiny outbound action helper.
- Do not extract `LineRichMenuTextTriggerResolver` further until more trigger modes exist.
- Do not split DTO/result classes by file size; they are already module-local and low-complexity.
