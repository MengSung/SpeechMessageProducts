# X02A Shared Cache Foundation Diagnostic Issues

Status: RUNTIME_VALIDATION_PENDING
Module: X02A
Workspace: X02A-shared-cache-foundation
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Nested agent count: 0
Issue document SHA-256: pending

## Scope Summary

X02A owns the shared cache foundation: `CacheKeys.cs`, `CacheService.cs`, and `ICacheService.cs`. This diagnostic covers cache interface/implementation, key construction, expiry defaults, and capacity base rules. It excludes B03 small-group-specific cache policy, X02B logging infrastructure, and X02C profiling except as dependency or consumer context.

## Ranked Issue List

### 1. X02A-PERF-001 Cache has expiry but no hard capacity baseline

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 78
- Confirmed: true
- Evidence confidence: 18
- Impact score: 22
- Likelihood/frequency score: 13
- Security urgency score: 0
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: X02A
- Cross-module: false
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Startup.cs:171`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:287`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:307`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:315`
- Evidence: `AddMemoryCache` configures compaction and expiration scan frequency but not `SizeLimit`; X02A cache entries set expiry/priority but no size; tracked keys grow until explicit remove or eviction callback.
- Control/data/lifetime flow: X01 registers process-wide memory cache and singleton `ICacheService`; X02A stores arbitrary consumer values and separately tracks keys in a singleton dictionary.
- Impact: X02A cannot state a hard cache memory/item budget, and high-cardinality consumers can expand cache/tracker footprint until expiry/eviction.
- Why this is necessary: the module map explicitly calls out X02A cache key/limit, memory, expiry, and eviction as validation concerns.
- Recommended action: establish a testable capacity base rule: size-limited entries, bounded tracker, or documented runtime budget with guard metrics.
- Validation: cache component tests plus representative B module load/smoke.
- Rollback boundary: DI cache registration and X02A cache service behavior.
- Extraction contract: capacity/expiry primitive.
- CCG round history:
  - Round 1: pending; source rechecked true

### 2. X02A-SEC-001 Raw identity-bearing cache keys are logged

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 68
- Confirmed: true
- Evidence confidence: 18
- Impact score: 17
- Likelihood/frequency score: 10
- Security urgency score: 13
- Performance gain score: 0
- Loop leverage score: 6
- Ease/reversibility score: 4
- Effort: S
- Primary owner: X02A
- Cross-module: X02B consumer context only
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs:69`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs:113`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs:120`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs:127`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:96`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:117`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:122`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:145`
- Evidence: X02A cache keys include raw account, LINE ID, contact ID, list ID, and date components; `CacheService` logs the full key on create/hit/miss/remove/eviction.
- Control/data/lifetime flow: consumer identifiers enter `CacheKeys`; the resulting key is used as `IMemoryCache` key and emitted to logger calls.
- Impact: debug/trace diagnostic logs can become an identifier leak surface.
- Why this is necessary: safe cache-key display/redaction is part of the shared cache foundation contract even though logger provider behavior belongs to X02B.
- Recommended action: log key category/prefix plus a short hash or redacted display value; forbid raw secrets and normalize user identifiers in shared key helpers.
- Validation: unit test/fake logger verifies raw identifiers are not emitted.
- Rollback boundary: X02A cache logging statements and key display helper.
- Extraction contract: cache key redaction/display primitive.
- CCG round history:
  - Round 1: pending; source rechecked true

### 3. X02A-EXT-001 Shared `CacheKeys` mixes reusable primitives with business/group-specific key policy

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 65
- Confirmed: true
- Evidence confidence: 18
- Impact score: 15
- Likelihood/frequency score: 11
- Security urgency score: 4
- Performance gain score: 4
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: X02A with B03/B06A/B01/B02 handoffs
- Cross-module: B03/B06A/B01/B02
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs:34`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs:69`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs:77`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs:91`
  - `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs:55`
  - `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs:80`
- Evidence: `CacheKeys` stores business/group/list/contact/report key builders and expiry constants in the shared cache foundation; the boundary map excludes small-group-specific cache policy from X02A.
- Control/data/lifetime flow: domain consumers call shared key builders directly, centralizing domain policy under X02A.
- Impact: future domain cache changes can land in X02A instead of module-owned key catalogs, slowing safe extraction and consumer-specific optimization.
- Why this is necessary: X02A should expose reusable key primitives, not own every domain key policy.
- Recommended action: define X02A key normalization/base namespace primitives and move module-specific catalogs behind owning modules through compatibility wrappers.
- Validation: ownership matrix for every current key builder and consumer tests for module-owned wrappers.
- Rollback boundary: additive wrappers first; no removal until consumer gates are green.
- Extraction contract: input/output/dependency/test seam/consumer.
- CCG round history:
  - Round 1: pending; source rechecked true

### 4. X02A-PERF-002 Async cache misses can stampede per key

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 62
- Confirmed: true
- Evidence confidence: 18
- Impact score: 14
- Likelihood/frequency score: 12
- Security urgency score: 0
- Performance gain score: 8
- Loop leverage score: 7
- Ease/reversibility score: 3
- Effort: M
- Primary owner: X02A
- Cross-module: false
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:171`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:180`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs:183`
- Evidence: `GetOrCreateAsync` checks cache, awaits the factory on miss, then sets the key; no per-key in-flight coordination is present.
- Control/data/lifetime flow: concurrent callers for the same cold key can pass `TryGet`, run duplicate factories, and write the same key.
- Impact: duplicate CRM/list work under concurrent load and less predictable cache performance.
- Why this is necessary: shared async cache semantics should be explicit for all consumers.
- Recommended action: add per-key single-flight semantics or document duplicate factory execution as intentional.
- Validation: concurrent component test with counted delayed factory.
- Rollback boundary: X02A async cache method only.
- Extraction contract: async cache primitive.
- CCG round history:
  - Round 1: pending; source rechecked true

## Runtime Validation Pending

- X02A-PERF-001 requires load/memory validation to size the actual capacity risk.
- X02A-PERF-002 requires concurrent component validation to quantify duplicate factory execution.
- X02A-SEC-001 requires fake/synthetic log capture to verify raw identifiers reach sinks under enabled log levels.
- X02A-EXT-001 requires ownership classification before any extraction change.

## Deleted Or Rejected Candidates

- `SmallGroupCacheManager` ownership by X02A: rejected. The map assigns it to B03 because it carries small-group-specific cache policy.
- Profiling implementation changes: rejected for X02A; belongs to X02C.
- Logger provider masking implementation: rejected for X02A; belongs to X02B after X02A stops emitting raw keys.

## Cross-Module Handoffs

- B03: own small-group cache manager and group-specific key/expiry policy.
- B06A/B01/B02: classify list/contact/member key catalogs if extracted from shared `CacheKeys`.
- X02B: validate log masking once X02A supplies redacted cache key display.
- X02C: validate runtime cost and memory baselines.
- X01: preserve DI lifetime compatibility for `ICacheService`.

## CCG Outcome Summary

Pending. The CCG prompt is `.ccg/dual-model-runs/x02a-issue-review-r1-input.md`. If no backend produces usable output, status must be changed to `DEGRADED_REVIEW_PENDING`.

## Final CCG Approval

Pending CCG review.
