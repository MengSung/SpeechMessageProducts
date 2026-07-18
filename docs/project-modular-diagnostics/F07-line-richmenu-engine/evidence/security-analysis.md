# F07 Security Analysis

Status: DEGRADED_CCG_REVIEW_APPLIED
Nested agent count: 0

## Confirmed Security / Integrity Findings

### F07-SEC-001: RichMenu TTL is exposed by policy decisions but never persisted, so temporary menus would not expire

Evidence:

- `LineMessagingProcessor.RichMenus/RichMenuDecision.cs:52` documents that TTL should be written into state for later sweep.
- `LineMessagingProcessor.RichMenus/RichMenuDecision.cs:56` exposes `Ttl`.
- `LineMessagingProcessor.RichMenus/RichMenuDecision.cs:75` accepts `ttl` in `Assign(...)`.
- `LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs:101` routes a selected menu key to assignment.
- `LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs:102` calls `AssignAsync(context.LineUserId, best.MenuKey!, cancellationToken)` without passing `best.Ttl`.
- `LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs:28` has no TTL or expiry parameter.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:143` writes `RichMenuUserState`.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:148` always uses `expiresAt: null`.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:65` enumerates states for expiry.
- `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:66` only returns states with `ExpiresAt.HasValue`.

Impact:

Policies can return a temporary RichMenu assignment that appears to have an expiry contract, but the assignment workflow stores it as non-expiring. The built-in text trigger policy currently does not pass a TTL, so this is primarily a latent contract defect until a custom policy or future built-in policy uses TTL. Once used, any menu intended to be temporary can remain linked indefinitely until another explicit assignment/unassignment occurs. This is a stale assignment and authorization-display risk when RichMenus represent role, membership, campaign, or operational state.

### F07-SEC-002: Same-menu assignment fast path trusts local state and skips LINE reconciliation

Evidence:

- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:120` reads previous state from `IRichMenuStateStore`.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:121` treats matching `CurrentMenuKey` as already assigned.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:123` returns `Linked(... changed: false)`.
- `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:136` is the provider link call path.
- Because of the return at `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:123`, `LinkRichMenuToUserAsync` at `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:137` is skipped.
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:101` registers the default state store as singleton in-memory state.

Impact:

The local state store is an auxiliary cache, but a same-menu assignment treats it as authoritative. If LINE provider state is changed out-of-band, if a durable custom store is stale, or if multi-node deployments diverge, F07 can report success without re-linking the user to the intended RichMenu. This can leave a user on a stale or default menu while the caller believes the correct menu is active.

### F07-SEC-003: Provisioning can reuse a remote menu created during a partial upload failure

Evidence:

- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:82` gets the provider RichMenu list once.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:88` builds `existingByName`.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:173` reuses a provider menu when the versioned name exists.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:177` upserts alias on reuse.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:180` may set that reused menu as default.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:194` creates a provider RichMenu when no name match exists.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:199` uploads the PNG only after creation.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:203` upserts alias only after upload.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:126` catches exceptions per definition.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:130` records only a failed item; no cleanup or quarantine of the just-created provider id is recorded.
- `LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs:138` reports `DeletedRichMenuIds` as empty.

Impact:

If `CreateRichMenuAsync` succeeds and `UploadRichMenuPngImageAsync` fails, the provider can retain a versioned RichMenu with the expected name but without confirmed uploaded content. A later sync sees the name match and follows the reuse path, which does not upload the image again. That can bind aliases/defaults/cache to a broken remote RichMenu and makes the failure sticky.

### F07-SEC-004: Public legacy workflow can delete the provider RichMenu currently linked to one user

Evidence:

- `LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs:40` exposes `DeleteLinkedRichMenuAsync`.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:152` implements `DeleteLinkedRichMenuAsync`.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:167` reads the current provider `richMenuId` for the user.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:168` unlinks that user.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:170` checks whether the id is present.
- `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:174` deletes the provider RichMenu id.
- `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:103` registers `ILineRichMenuWorkflow` publicly.
- `LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs:83` asserts the request sequence.
- `LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs:86` asserts `DELETE /v2/bot/richmenu/rich-menu-001`.

Impact:

LINE RichMenus are provider/channel resources and can be shared through aliases, defaults, or multiple user links. Deleting the RichMenu merely because it was linked to one user can remove a shared menu for other users or invalidate aliases/defaults. The newer assignment workflow avoids this destructive provider operation for unassign, but the legacy workflow remains exposed through public interface and DI.

## Non-Issues / Bounded Areas

- Token handling: F07 does not construct tokens or log channel tokens. `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs:37` notes that F07 uses the processor abstraction instead of directly creating HTTP clients or tokens. Token configuration appears in read-only F05B composition at `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:60`.
- Direct logging exposure: no `ILogger`, `Trace`, `Console`, or `Debug` logging paths were found in F07 production source. Result objects do expose provider exception messages, but F07 does not log them by itself.
- Callback validation: F07 only creates outbound richmenu switch actions in `RichMenuActionFactory`. It validates non-empty alias and postback data at `LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs:42` and `LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs:47`; inbound postback authorization/parsing is outside F07 owned scope.
- Cross-user storage keying: state is keyed by normalized `lineUserId` in `LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs:46`, and provider link/unlink uses normalized user ids in `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:102` and `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs:183`. No direct shared mutable current-user field was found in F07 source.
