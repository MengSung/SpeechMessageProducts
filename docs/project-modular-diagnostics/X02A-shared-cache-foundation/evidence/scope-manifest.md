# X02A Scope Manifest

Leaf ID: X02A
Workspace: `docs/project-modular-diagnostics/X02A-shared-cache-foundation/`
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Boundary Source

- Workflow: `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`
- Map row: X02A owns shared cache foundation: cache interface/implementation, cache key, capacity/expiry base rules.
- Explicit exclusions: group-specific cache policy, logging, profiling except as dependency/consumer context.
- Gate state: known gate-blocked. X02A requires cache component tests plus at least one representative B module load/smoke before optimization can be declared complete.

## Primary Owner Files

- `SpeechMessageProducts.ChurchReport/Services/Caching/CacheKeys.cs`
- `SpeechMessageProducts.ChurchReport/Services/Caching/CacheService.cs`
- `SpeechMessageProducts.ChurchReport/Services/Caching/ICacheService.cs`

## Non-Owned Context Files

- `SpeechMessageProducts.ChurchReport/Startup.cs` registers `AddMemoryCache`, `ICacheService`, and the B03 small-group cache manager.
- `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs` consumes `ICacheService` and X02A cache keys.
- `SpeechMessageProducts.ChurchReport/Services/Caching/ISmallGroupCacheManager.cs` is excluded by the module map and belongs to B03.
- `SpeechMessageProducts.ChurchReport/Services/Caching/SmallGroupCacheManager.cs` is excluded by the module map and belongs to B03.
- `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Cache.cs` is B03 consumer context.

## Consumers And Dependencies

- Dependencies: host DI/lifetime from X01, CRM/list data consumers from F03A/B06A/B03, memory cache infrastructure from ASP.NET Core.
- Consumers: all host modules through the cache infrastructure contract; representative concrete consumers include B03 small-group flows and B06A list/reference flows.
- Cross-module handoff: group-specific cache policy remains B03; runtime logging/health/profiling remains X02B/X02C.

## Evidence Snapshot

- `CacheService.GetOrCreate`, `Set`, and `GetOrCreateAsync` validate non-empty keys, set default absolute/sliding expiry, and track keys in a `ConcurrentDictionary`.
- `CacheService` logs full cache keys at Debug/Trace levels on create/hit/miss/remove/eviction.
- `CacheService.GetOrCreateAsync` performs `TryGet`, awaits the factory on miss, then calls `Set`; no per-key single-flight or lock is present.
- `Startup.cs` registers memory cache with compaction and expiration scan frequency but intentionally does not configure `SizeLimit`; `CacheService` does not assign entry `Size`.
- `CacheKeys` includes business-specific prefixes and builders including account, Line ID, contact ID, multi-group list, weekly report, chart data, members, dropdown, integrate data, present record, and list entity.

## Write-Scope Statement

Only files under `docs/project-modular-diagnostics/X02A-shared-cache-foundation/**` and x02a-prefixed files under `.ccg/dual-model-runs/**` are intended to be created or updated. Product code, project files, configs, tests, generated files, bin/obj, caches, lockfiles, and ledger files are out of scope.
