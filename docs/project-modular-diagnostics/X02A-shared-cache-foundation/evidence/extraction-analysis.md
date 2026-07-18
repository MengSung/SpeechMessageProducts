# X02A Extraction And Acceleration Analysis

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Extraction Boundary

X02A should provide reusable cache primitives: interface, implementation, key construction conventions, and base expiry/capacity rules. B03 owns small-group-specific cache policy, while X02B/X02C own logging/profiling.

## Findings

### X02A-EXT-001 Shared `CacheKeys` mixes reusable primitives with business/group-specific key policy

- Severity: Medium
- Confidence: High
- Primary owner: X02A with handoffs to B03/B06A/B01/B02
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs`
  - `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs`
- Evidence:
  - `CacheKeys` contains `MultiGroupList`, `WeeklyReport`, `ChartData`, `Members`, `Dropdown`, `IntegrateData`, `ContactByAccount`, `ContactByLineId`, `ContactById`, `PresentRecord`, and `ListEntity`.
  - The module boundary explicitly excludes small-group-specific cache policy from X02A.
  - `ListManagerCacheExtensions` uses X02A keys for multi-group list and integrate-data flows, while small-group-specific cache manager is separately assigned to B03.
- Impact:
  - Business-specific key naming and expiry rules are centralized in the shared foundation, making ownership ambiguous.
  - Consumers can add domain policy to X02A instead of defining owned policy in their module.
  - Extraction/optimization of B03/B06A cache behavior becomes harder because policy and infrastructure are interleaved.
- Recommended action:
  - Split the contract conceptually before code changes:
    - X02A: key normalization helpers, base namespacing, redacted display, default expiry/capacity primitives.
    - B03/B06A/etc.: domain key catalogs and domain expiry policy.
  - Preserve binary/source compatibility during any later optimization by adding module-owned wrappers before removing shared business keys.
- Validation:
  - Ownership matrix showing each current key builder's target module.
  - Component tests for X02A key normalization and consumer tests for module-owned key catalogs.

## Acceleration Opportunities

- A clean X02A cache-key primitive would reduce repeated ad hoc key formatting across consumers.
- A redacted key display helper can solve both security logging and diagnostics readability.
- A single-flight async primitive can become a reusable acceleration point for CRM-heavy consumers without embedding CRM policy in X02A.

## Rejected Extraction Candidates

- Moving `SmallGroupCacheManager` into X02A is rejected; the module boundary explicitly assigns its small-group policy to B03.
- Moving logging/profiling responsibilities into X02A is rejected; X02A should expose safe metadata but not own logger provider or profiling implementation.
