ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p74-memberinfo-commitment-metadata-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 MemberInfo Commitment Metadata Read Boundary — Analysis Request

Review the proposed local-only, disabled-by-default P7.4 child. Do not suggest CE, traffic, feature enablement, P7.5, P8, ToolUtility removal, or request-time legacy fallback.

## Existing facts

- The legacy `MemberInfoCommitmentTypeMetadataProvider` uses `IOrganizationService` plus one process-global cache key and locale preference 1028 -> 2052 -> UserLocalizedLabel.
- Existing P7.3 `IPackage03SpecialResourceClient.RetrieveOptionSetAsync` exposes only the closed `MetadataOptionSetTarget.ContactCustomerTypeCode` target and bounded pure DTOs `(Value, Label, ConfiguredOrder)`.
- Data8 already owns a profile/generation/target/locale-bounded metadata cache. ChurchReport must not add another shared metadata cache.
- Existing `Package03SpecialResourcesEnabled` is a false default image gate. The metadata consumer must have an independent false default sub-gate and must not enable the image route.
- `SearchDistrictTree`, `LoadGroupMembers`, and `LoadUngroupedMembers` consume the metadata for text matching, configured sort segments, and display labels. Current first two are synchronous; the ungrouped action is async.
- A true metadata gate must dispatch once through the typed client with a fixed deployment ProfileAlias, fixed server workload, `MetadataOptionSetTarget.ContactCustomerTypeCode`, and `HttpContext.RequestAborted`; timeout, fault, cancellation, malformed DTO, or unavailable client must not retry or fall back to legacy.

## Proposed direction

1. Add `DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled`, checked-in false, composed with `Package03SpecialResourcesEnabled` as its base gate.
2. Add direct bootstrap predicate/factory tests for false, base-only, both gates plus non-empty profile, and profile failure before host resolution.
3. Add a request-local `Package03MemberInfoCommitmentMetadataReadService` that validates non-null bounded options, unique values/orders, `ConfiguredOrder` exactly 0..N-1, nonblank <=512-char labels, defensive copies, and forwards cancellation unchanged.
4. Convert the three affected MemberInfo actions to async where needed and load one consistent metadata option snapshot per request: legacy provider only while gate=false; typed-only while true. Pass that snapshot into sort/search/row construction. No ChurchReport cache, no Entity rehydration, no fallback/retry.
5. Keep all flags false and do local contract/lifecycle/AB-isolation tests only.

Return Critical / Warning / Info findings. Focus on request isolation, cancellation/resource ownership, deployment gate composition, existing MVC behavior, and whether the scope incorrectly claims migration or evidence.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.