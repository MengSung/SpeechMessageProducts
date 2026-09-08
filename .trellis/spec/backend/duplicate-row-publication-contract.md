# Duplicate Row Publication Contract

## 1. Scope / Trigger

Apply this contract whenever code creates, caches, transforms, serializes, renders,
exports, or mutates a repeatable data collection. It covers ASP.NET Core actions,
DevExtreme grids, Razor tables, trees, reports, JSON DTOs, background results,
Dataverse projections, and any future list-like UI.

The protected failure is not merely two equal display names. The failure is two
rows representing one authoritative record/business event, a partially published
collection, or a stale/cross-scope snapshot. Legitimate same-name records with
different stable identities remain visible.

## 2. Signatures

A safe publication boundary has the following conceptual inputs and output:

```text
GetOrBuildDetachedRead(
    ValidatedSubject subject,
    ValidatedTenant tenant,
    AuthorizedScope scope,
    DateOrVersion generation,
    CancellationToken requestCancellation)
    -> DetachedReadSnapshot
```

Each repeatable row exposes one server-owned stable identity such as
`PresentRecordId`, `ContactId` only when the contact itself is the row, or a
server-generated business-event key. UI components configure that same field as
their row key.

## 3. Contracts

- The server validates subject, tenant/organization, authorization, list/report
  scope, date/version/generation, and authentication epoch before data access.
- A candidate is operation-local. All I/O, mapping, sorting, normalization, and
  row-key validation complete before a single atomic reference publication.
- Readers receive immutable values or deep-enough request-owned copies. They do
  not enumerate a mutable Session/cache list.
- Row-key validation runs on each exact collection passed to a serializer/grid.
  Different UI collections may contain the same record intentionally; duplicate
  validation is scoped to one consumer collection.
- Different stable keys with the same display name are valid and preserved.
  Repeated non-empty stable keys in one consumer collection fail closed.
- Cache entries contain no unpartitioned user data. Cache lifetime and capacity
  are bounded, and eviction never disposes a synchronization primitive that is
  still held or awaited.
- Application single-flight prevents in-process duplicate builds. Database or
  Dataverse alternate/idempotency keys prevent cross-process duplicate writes.

## 4. Validation & Error Matrix

| Condition | Required result |
|---|---|
| Same FullName, different stable keys | Return both rows |
| Same non-empty stable key twice in one grid source | Reject candidate; log only non-sensitive diagnostics |
| Empty key for a persisted row | Reject candidate or assign a documented server-owned draft key before publication |
| Candidate loader throws or times out | Do not publish; preserve previous complete snapshot; permit retry |
| User/tenant/list/date/generation changes | Treat as a new scope; never return the previous scope snapshot |
| Caller mutates returned list | Published snapshot remains unchanged |
| Concurrent same-scope requests | At most one build; all successful readers see one complete generation |
| Concurrent different-scope requests | No mutable state, credential, row, or authorization decision crosses scopes |
| Existing database/Dataverse conflict count > 1 | Stop creating; report conflict for auditable remediation |

## 5. Good / Base / Bad Cases

- Good: two contacts named 王小明 have different `PresentRecordId` values; both rows
  render and can be edited independently.
- Base: 32 AJAX requests request the same authorized scope; one loader builds a
  candidate and every response receives a detached copy of the same generation.
- Bad: two loaders append to a Session-owned `List<Member>` while
  `DataSourceLoader` enumerates it.
- Bad: `.DistinctBy(member => member.FullName)` makes the screenshot look correct
  while silently deleting a legitimate person.
- Bad: an in-process semaphore is treated as the uniqueness guarantee for two IIS
  workers writing the same Dataverse business event.

## 6. Tests Required

- Same-name test: literal fixtures with equal display names and different stable
  keys; assert both remain and preserve their own values.
- Exact-key test: duplicate stable key in the actual consumer collection; assert
  publication fails and the previous snapshot is unchanged.
- Single-flight test: deterministic barrier with at least 32 concurrent calls;
  assert one loader invocation and complete equal generations.
- Scope test: change user/contact, organization, list/report, date/version, and
  authentication epoch independently; assert each change invalidates the hit.
- Failure/retry test: inject timeout/exception after partial candidate assembly;
  assert no partial publication and the next call succeeds.
- Mutation-isolation test: mutate one returned list/member; assert Session/cache
  and a second response remain unchanged.
- Lifecycle test: after cancellation, completion, eviction, or disposal, assert
  active builds/waiters/leases/registrations return to the declared baseline and
  retained memory does not trend upward under repeated generations.
- Cross-process writer test where applicable: repeated idempotency key from two
  application instances yields one canonical active business record.

## 7. Wrong vs Correct

### Wrong

```csharp
var rows = sessionReport.Members.DistinctBy(x => x.FullName);
return DataSourceLoader.Load(rows, options);
```

This hides legitimate same-name people, reads mutable Session state directly, and
does not prevent concurrent or cross-process duplicate creation.

### Correct

```csharp
var candidate = BuildOperationLocalCandidate(validatedScope);
ValidateUniqueStableKeys(candidate.Rows);
PublishCompleteSnapshot(candidate, validatedScope);
return DataSourceLoader.Load(candidate.CreateDetachedRows(), options);
```

The real implementation may use a scoped holder, immutable snapshot, or another
equivalent mechanism, but it must preserve the same isolation, atomic publication,
stable identity, retry, cleanup, and performance contracts.
