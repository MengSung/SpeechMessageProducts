# Technical design

## Boundaries

The audit covers the committed ChurchReport build, middleware, authentication, controller-base, payment utility, and lifecycle changes. Product behavior remains unchanged except for correcting demonstrably corrupted money-to-Chinese mappings. Further optimization is limited to changes with a measurable hot path and explicit isolation ownership.

## Data and lifecycle rules

- Request/session/identity data stays request-scoped or is keyed by the complete validated session boundary.
- Process-wide caches contain only bounded, non-sensitive metadata or use a hard upper bound and deterministic stale-entry removal.
- `HttpClientFactory` clients carry per-request authorization only on `HttpRequestMessage`; no token is stored in default headers or shared state.
- Static filters remain singleton only when immutable and request data is exclusively supplied through the filter context.
- Static assets may be long-cacheable only after path and extension allow-list checks; dynamic responses retain `no-store` and cookie variance.

## Implementation approach

1. Establish a baseline with build/tests and inspect the exact commit diff.
2. Repair and test `MoneyToChinese` using a small deterministic conversion algorithm that preserves the existing method contract.
3. Verify changed middleware/filter/HTTP/lifecycle paths with focused tests or executable checks; fix only proven defects.
4. Evaluate further acceleration candidates, especially explicit Dataverse column sets, and implement only a bounded candidate whose consumers are fully known.
5. Run full verification, byte-level encoding checks, and external review artifacts.

## Compatibility and rollback

The working tree is already at the Claude commit. All new edits remain uncommitted until verification. A rollback is a file-level revert of only this task's edits; no destructive reset is permitted. Existing 22 reflection-test failures must be compared against the parent baseline and not silently reclassified.

## Risk decisions

The corrupted money conversion is release-blocking. ContextDictionary's static timer and diagnostic/token logging are review warnings unless a reproducible leak or secret disclosure is demonstrated; any such evidence upgrades them to Critical. ColumnSet narrowing is deferred unless a complete field-usage inventory and regression tests exist.
