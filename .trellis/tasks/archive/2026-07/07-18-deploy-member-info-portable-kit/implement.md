# MemberInfo Portable Migration Implementation Plan

## Execution Rules

- Work inline in this session; do not dispatch implementation or review subagents.
- Follow TDD for every behavior change: add/adapt one focused test, run it and confirm the expected failure, then add the minimum production change and rerun.
- Treat `docs/portable/member-info-portable-kit/reference-implementation/host-integration/*.patch` as comparison evidence only. Never run `git apply` against this worktree.
- Preserve all user-provided untracked portable/spec files and the current host-specific changes listed in `design.md`.
- Do not invoke Gemini or Claude. Record the owner-approved quota waiver and complete local zero-trust review.

## Phase 1: Freeze Baseline And Paths

- [ ] Record branch, HEAD, status, target framework, server/client DevExtreme versions, serializer, auth sources, CRM field assumptions, and current endpoint inventory in `.ccg/tasks/deploy-member-info-portable-kit/inventory.md`.
- [ ] Re-run package verifier in read-only mode.
- [ ] Re-run the untouched MemberInfo test suite and save exact inherited pass/fail totals.
- [ ] Confirm the new tests use `SpeechMessageProducts.ChurchReport` paths rather than historical `ChurchReport` project paths.

## Phase 2: Core Tree Contracts And Pure Services

**Tests to create/adapt first:**

- `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoCurrentContactCounterTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardListTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs`
- `ChurchReport.MemberInfo.Tests/RelationGoalFormatterTests.cs`

**Production files:**

- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoCurrentContactCounter.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/RelationGoalFormatter.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs`
- `SpeechMessageProducts.ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`

- [ ] Add failing tests for authoritative list-ID validation, Church/Shepherd access resolution, complete group counts, unassigned-district ordering, metadata trimming, current-contact deduplication, authorized search ordering, and relation-goal formatting.
- [ ] Run the focused tests and confirm failures are due to missing contracts/behavior.
- [ ] Implement the minimum pure services/DTOs and rerun until green.
- [ ] Commit the backend contract/service batch with a Traditional Chinese subject and body.

## Phase 3: Controller Tree, Search, Ungrouped, And Detail Integration

**Tests to create/adapt first:**

- `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoDetailContractTests.cs`

**Production files:**

- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `SpeechMessageProducts.ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- `SpeechMessageProducts.ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`

- [ ] Add failing static/controller contracts for `LoadDistrictTree`, `LoadGroupMembers`, `LoadUngroupedMembers`, `SearchDistrictTree`, valid-list filtering, chunked authorization, one-query group metadata, PascalCase rows, gender, birth date normalization, and single `RelationGoals` data.
- [ ] Run focused contracts and confirm expected RED results.
- [ ] Add controller routes/helpers and detail mapping while preserving current constructor, LINE processor, popup upload, avatar, and update behavior.
- [ ] Verify no new per-row/per-group CRM query path and no fail-open closed-status path.
- [ ] Rerun focused tests and build the application.
- [ ] Commit the controller/detail batch with a Traditional Chinese subject and body.

## Phase 4: Tree UI, Search Lifecycle, Responsive Columns, And Touch

**Test to create/adapt first:**

- `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`

**Production file:**

- `SpeechMessageProducts.ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] Add failing contracts for tree-only initial load, lazy group/member/avatar requests, Church-only ungrouped node, one-group auto-expand, loading/search cancel/restore state, XSS-safe text binding, shared nine-column factory, exact order/captions/alignment, 72px/62px fixed columns, widget resizing, single sorting, remote `RelationGoals` guard, responsive tokens, and 22.1.6 fixed-row touch scope.
- [ ] Run the focused view tests and confirm RED.
- [ ] Replace the legacy flat-grid shell with the tree/search UI while retaining resync and detail-popup upload behavior.
- [ ] Extract the Razor script, replace the single server boolean expression, and run `node --check`.
- [ ] Rerun focused view tests and application build.
- [ ] Commit the frontend batch with a Traditional Chinese subject and body.

## Phase 5: Dynamics Metadata Ordering And Remote Segment Paging

**Tests to create first:**

- `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeCountQueryTests.cs`
- updates to tree controller/search/view contracts.

**Production files:**

- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`
- `SpeechMessageProducts.ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `SpeechMessageProducts.ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] Add RED tests proving metadata collection index, not raw value/label, defines rank; Configured/Unknown/Empty remain distinct; descending reverses configured ranks only; stable name/contact ordering and cross-segment slice planning are correct.
- [ ] Add RED query tests proving aggregate conversion preserves base filters/link entities and removes page/count/order.
- [ ] Implement provider/cache, sorter/slicer, count query, DTO flags/rank, authorized local/search sorting, segmented ungrouped retrieval, and visible-column local/remote selectors.
- [ ] Prove `useraworderby`, raw `customertypecode` ordering, and visible raw fields are absent from the three grid paths.
- [ ] Rerun all focused metadata/controller/search/view tests and builds.
- [ ] Commit the metadata-order batch with a Traditional Chinese subject and body.

## Phase 6: Full Verification And Inline Review

- [ ] Run the portable package verifier.
- [ ] Run all focused MemberInfo tests and the complete MemberInfo test project; classify every failure as inherited or introduced.
- [ ] Build `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj` and `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`.
- [ ] Run Razor JavaScript syntax validation, strict UTF-8 decoding, U+FFFD scan, secret/privacy scan, `git diff --check`, status/stat, and scoped diff review.
- [ ] Start the local site if configuration permits without changing production settings; use browser checks for desktop and 320/390/430/640 widths, console/network errors, tree/search/detail flows, single horizontal scrollbar, fixed columns, resize/sort, and touch simulation.
- [ ] Record real-environment-only CRM/role/LINE/mobile checks as pending user verification if they cannot be reproduced safely.
- [ ] Perform an inline zero-trust review against every PRD acceptance criterion; fix valid findings with a new failing test and rerun affected/full checks.
- [ ] Write `.ccg/tasks/deploy-member-info-portable-kit/review.md` with exact evidence and the external-review waiver.

## Phase 7: Commit, Push, And Archive

- [ ] Commit final evidence/task/spec updates with a Traditional Chinese subject and body.
- [ ] Push `1.0.0.1.WorkTreeMemberInfo` and verify the remote branch points at local HEAD.
- [ ] Run Trellis spec-update assessment; update specs only for reusable project knowledge learned during migration.
- [ ] Archive the Trellis task and CCG task, committing archive moves as required.
- [ ] Verify the worktree is clean except for any explicitly preserved, intentionally uncommitted user artifact that cannot be committed safely.

## Rollback Points

- Revert only the latest coherent batch if a phase cannot be made green.
- Never delete or reset the portable kit/spec inputs.
- Never use `git reset --hard` or overwrite unrelated user changes.
- No production release or external CRM mutation is performed by this plan.
