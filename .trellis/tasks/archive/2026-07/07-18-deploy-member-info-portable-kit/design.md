# MemberInfo Portable Migration Design

## Architecture

The migration keeps the existing `SpeechMessageProducts.ChurchReport` host and adds narrowly scoped MemberInfo services and DTOs under the existing `ChurchReport` namespace. `MemberInfoController` remains the integration boundary for access resolution, CRM queries, cache ownership, and route contracts. `MemberInfoGrid.cshtml` replaces the flat grid with a tree/search state machine while reusing the existing protected avatar and detail-popup capabilities.

## Component Boundaries

### Pure domain and query helpers

- `Services/MemberInfo/DistrictTreeInputs.cs`: normalized descriptors consumed by the tree builder.
- `Services/MemberInfo/DistrictTreeBuilder.cs`: deterministic district/group hierarchy, counts, sort order, and metadata trimming.
- `Services/MemberInfo/MemberInfoCurrentContactCounter.cs`: distinct active/non-closed contact counts.
- `Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`: authorized, deduplicated search rows and membership-rank ordering.
- `Services/MemberInfo/RelationGoalFormatter.cs`: stable, deduplicated combined relation/goal text.
- `Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs`: finite-TTL schema-only metadata rank cache.
- `Services/MemberInfo/MemberInfoCommitmentTypeSort.cs`: Configured/Unknown/Empty ordering and remote slice planning.
- `Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs`: aggregate count-query transformation preserving base filters.

These units contain no user/session cache and are covered by focused tests.

### Data contracts

- `ViewModels/MemberInfoTree/DistrictTreeViewModels.cs` owns PascalCase tree/member/search DTOs.
- `ViewModels/MemberInfoDetailViewModel.cs` adds read-only `Gender` and nullable `BirthDate` while preserving existing editable fields.

The visible member row keeps the label `MembershipStatus` plus non-visible `MembershipStatusOrder` and `HasMembershipStatusValue`. No raw option value becomes a visible grid field.

### Host integration

`MemberInfoController` adds four read routes: tree skeleton, group members, Church-only ungrouped members, and authorized search. Existing detail/image/upload/resync/update routes remain. The controller also owns list descriptor fetching, valid-list calculation, chunked contact authorization, relation queries, membership metadata integration, ungrouped aggregate segmentation, and cache invalidation.

The migration preserves current host differences:

- constructor/base chaining continues without the removed `IPayment` dependency;
- LINE profile lookup continues through `LineMessagingProcessorClass`;
- existing popup upload toolbar and stale-detail-response token remain;
- current global no-cache/session authorization filters remain unchanged;
- source paths use `SpeechMessageProducts.ChurchReport`, while namespaces remain `ChurchReport`.

## Data Flow

1. `Index` resolves access and renders only the page shell.
2. `LoadDistrictTree` resolves valid lists, fetches one list descriptor set including group time/place, computes authorized non-personal skeleton metadata, and returns PascalCase DTOs.
3. Expanding a group calls `LoadGroupMembers`; the server validates the list ID against the authoritative visible set, retrieves members in pages/chunks, batch-authorizes contacts, maps rows, attaches relations, and sorts by metadata rank.
4. Church-only `LoadUngroupedMembers` preserves current-contact/search/group-exclusion filters, counts membership segments, projects global skip/take into segment slices, and retrieves only required page rows.
5. `SearchDistrictTree` builds candidates from approved fields, batch-authorizes and deduplicates before response, then applies the same membership sort.
6. The page builds one shared nine-column factory for normal groups, ungrouped remote paging, and search results. Protected batch-avatar loading runs after visible rows render.

## Authorization And Failure Behavior

- Unknown access, unauthorized list/contact, malformed GUID, missing closed-status metadata, and CRM authorization-query failures fail closed.
- Church access is not permission to use arbitrary non-empty list IDs; requested IDs must exist in the valid active/app-named/purpose-filtered set.
- Shepherd visible lists are the intersection of valid lists and the user's ListManager records.
- User-specific tree/search data is never stored in shared cache.
- Metadata failure never falls back to raw option integer ordering. Data remains available with diagnostic/fallback ordering, and runtime acceptance records the degraded capability.

## Caching And Performance

- Church skeleton/grouped-ID snapshot may use a three-minute shared cache because it contains no personal rows.
- Shepherd skeleton and all search results are uncached.
- Metadata cache keys are schema/organization scoped and contain no personal data; success and transient failure use separate finite TTLs.
- Contact authorization, list membership, relations, and images are retrieved in chunks/pages; no new per-row or per-group CRM query is introduced.
- Ungrouped paging retrieves only slices needed for the requested page and never loads the whole church into memory.

## Frontend Compatibility

The loaded client asset is DevExtreme 22.1.6. Therefore the final UI may use the kit's 22.1.6-scoped fixed-row touch bridge, but only after contract tests assert its selectors and event scope. Headers stay under native DevExtreme resize/sort handling. The server wrapper package difference (`23.1.5`) does not override the actual browser client version.

## Rollout And Rollback

Changes are committed in independent batches: planning/evidence, backend contracts/services, controller integration, frontend/detail integration, metadata ordering, and final evidence/archive. Any batch can be reverted normally without deleting the portable inputs. No production publish or CRM write is part of this task.

## Verification Strategy

- RED/GREEN focused xUnit tests per helper and contract.
- Full `ChurchReport.MemberInfo.Tests` with inherited failures separately classified.
- `dotnet build` for the application and MemberInfo test project.
- Razor script extraction plus `node --check`.
- Package verifier, strict UTF-8/U+FFFD scan, privacy/secret scan, `git diff --check`, and exact-scope review.
- Browser checks at desktop and 320/390/430/640 widths when a safe runnable environment is available.
- Inline zero-trust review only; Gemini/Claude are not called because the owner waived exhausted providers.
