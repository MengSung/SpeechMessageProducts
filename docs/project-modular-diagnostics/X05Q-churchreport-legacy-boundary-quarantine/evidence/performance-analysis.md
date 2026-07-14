# X05Q Performance Analysis

Module: X05Q
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## PERF-001 Repeated ListManager And Session Rehydration

Evidence:

- `Controllers/BaseChurchController.cs:641-764` can lazily rebuild `InMemoryContext.ListManager` from session account/password data.
- `Extensions/ListManagerCacheExtensions.cs:40-83` adds a cache layer for `SetupListManager`, keyed by account and date.
- `Extensions/ListManagerCacheExtensions.cs:118-139` invalidates cache by account/list prefixes.
- `Controllers/BaseChurchController.cs:705-734` calls cleanup around session/cache keys when session and ListManager password diverge.

Performance impact:

The current shape suggests the same legacy identity/list setup is performed in multiple places: session validation, lazy rebuild, cache extension setup, and controller-level view setup. Even with caching, the key material and invalidation are spread across the base controller and extension methods. That increases repeated conversions and makes cache misses expensive because the miss path calls the legacy CRM-backed setup again.

Optimization candidate:

Batch validation and hydration behind a single adapter that returns a typed `LegacyUserContext` with cache metadata. That would allow one lookup per request and make cache invalidation deterministic.

## PERF-002 WebServiceConnector N+1 And Materialization Risk

Evidence:

- `WebServiceConnector/ChurchListDataProcessor.cs:260-264`, `:346`, `:451`, `:494`, `:543`, `:577`, `:649`, and `:700` contain nested loops over CRM entity collections and in-memory lists.
- `WebServiceConnector/AppointmentsDownUpLoader.cs:219`, `:317`, `:382`, `:1811`, and `:1835` loops through appointment/member/report collections.
- `WebServiceConnector/DownloadHappyGroup.cs:298-302`, `:342`, `:374`, `:421`, `:499`, and `:550` iterate list/member collections.
- `WebServiceConnector/UploadIntegrateData.Contact.cs:36`, `:60`, `:246`, `:325`, and `:448` iterate member and present-record collections.

Performance impact:

The legacy connector layer repeatedly converts CRM `EntityCollection` objects into business objects and cross-checks them with other in-memory collections. Without a module-owned query contract, X05Q cannot prove which loops are necessary and which are accidental N+1 behavior.

Optimization candidate:

Define batch query contracts for the high-volume flows: list hierarchy, member identity, present records, and weekly reports. Extraction should start with pure converter functions plus explicit selected columns, then move CRM calls behind query services.

## PERF-003 Synchronous Upload Lock And Read-After-Write Reload

Evidence:

- `WebServiceConnector/UploadIntegrateData.Core.cs:80` declares a static upload lock.
- `WebServiceConnector/UploadIntegrateData.Core.cs:92-143` prepares upload state and enters the upload flow.
- `WebServiceConnector/WeeklyReportManager.cs:296-328` uploads a weekly report and returns `DownloadWeeklyReport`, creating an immediate read-after-write reload.

Performance impact:

The static lock serializes upload work across requests in the process. The immediate reload after upload increases CRM IO and latency on the write path. This may be necessary for consistency, but the boundary does not document why all uploads share one process-wide lock or which data requires post-write reload.

Optimization candidate:

Replace the process-wide lock with a keyed concurrency policy by list/week/report where possible, and split post-write reload into a validation/audit contract that can fetch only the changed records.

## Runtime Measurement Needs

No benchmarks or tests were run because this diagnostic is read-only and must not write `bin`, `obj`, generated output, cache, or lockfiles. Runtime validation should measure request count, CRM call count, cache hit ratio, and wall time around the listed methods.
