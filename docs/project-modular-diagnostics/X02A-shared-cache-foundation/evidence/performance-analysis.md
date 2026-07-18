# X02A Performance And Design Analysis

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Performance Boundary

X02A owns shared cache implementation, cache key, and capacity/expiry base rules. Profiling infrastructure belongs to X02C, but X02A must document hypotheses that require runtime validation.

## Findings

### X02A-PERF-001 Cache has expiry but no hard capacity baseline

- Severity: High
- Confidence: High
- Primary owner: X02A
- Files:
  - `SpeechMessageProducts.ChurchReport/Startup.cs`
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs`
- Evidence:
  - `Startup.cs` configures `AddMemoryCache` with `CompactionPercentage = 0.10` and `ExpirationScanFrequency = TimeSpan.FromMinutes(5)`.
  - The same registration comments state `SizeLimit` is intentionally not configured.
  - `CacheService.ConfigureCacheEntry` and `ConfigureCacheEntryOptions` set absolute/sliding expiry and priority but do not set entry size.
  - `_trackedKeys` retains one entry per tracked cache key until explicit remove or eviction callback.
- Impact:
  - Cache entries and key tracking can grow with key cardinality until expiry/eviction happens.
  - The shared cache foundation cannot currently state a hard memory or item-count budget, which blocks X02A optimization completion under the module map's cache key/limit baseline requirement.
- Recommended action:
  - Establish an explicit capacity rule for X02A: either size-limited memory cache with required entry size, a bounded key tracker, or a documented runtime budget with guard metrics.
  - Keep group-specific capacity choices outside X02A, but expose primitives that consumers can use consistently.
- Validation:
  - Cache component test for size/eviction behavior.
  - Load baseline with high-cardinality keys and representative B module smoke.

### X02A-PERF-002 Async cache misses can stampede per key

- Severity: Medium
- Confidence: High
- Primary owner: X02A
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs`
- Evidence:
  - `GetOrCreateAsync` calls `TryGet`; on miss it awaits `factory()` and then calls `Set`.
  - There is no per-key in-flight task cache, semaphore, or `GetOrCreateAsync` coordination around the factory.
- Impact:
  - Concurrent requests for the same cold key can run duplicate CRM/list factories and overwrite the same cache key.
  - This risk affects consumers relying on cache to reduce expensive CRM or list setup calls.
- Recommended action:
  - Add single-flight coordination for async `GetOrCreateAsync`, or document that factories must tolerate duplicate concurrent execution.
  - Include cancellation and exception cleanup semantics in the shared cache contract.
- Validation:
  - Component test with N concurrent callers for one key proving the factory executes once or proving documented duplicate behavior.

## Rejected Performance Candidates

- Prefix invalidation is not by itself a confirmed bug. It is O(n) over tracked keys and should be covered by the capacity baseline, but current evidence does not prove it is a hot-path bottleneck.
- Small-group cache manager internals are B03, not X02A, except as consumer evidence.

## Runtime Evidence Needed

- Maximum key cardinality under representative B module workflows.
- Memory growth and eviction behavior during repeated list/group/report flows.
- Concurrent miss rate for async cache consumers.
