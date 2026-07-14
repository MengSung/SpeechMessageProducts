# F03A Performance Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY

## Confirmed Findings

### F03A-PERF-001 Synchronous CRM Calls Sit Behind Task APIs

Multiple API families use the same behavior:

- `CollectionQueryService.cs:386-405`
- `CrmAsyncExtensions.cs:218-253`
- `EntityOptimizedQueryService.cs:353-376`
- `ContactService.cs:409-428`

Each helper checks cancellation, invokes the synchronous operation, then
returns `Task.FromResult`, `Task.CompletedTask`, or a faulted task. The network
wait has already occurred on the caller thread. In addition,
`ToolUtilityClass.Entity.cs:68-78` and `:172-182` are `async` methods without an
await and directly call synchronous CRM Create/Update.

The list APIs choose the other common wrapper:
`ListService.cs:365-412`, `:433-494`, and `:520-560` use `Task.Run` around
synchronous `Execute` calls. This frees the immediate caller thread but
occupies a ThreadPool worker for the full network wait and cannot cancel the
in-flight CRM operation.

Cost flow:

`async` caller -> synchronous `IOrganizationService` call on caller/worker ->
remote latency consumes a managed thread -> completed Task -> continuation.

Counter-evidence: avoiding `Task.Run` in the first group correctly avoids extra
scheduling overhead, and the list service uses actual SDK batch requests.
Neither guard makes the I/O asynchronous. A real native async implementation
exists in `Adapters/DataverseServiceClientAdapter.cs:142-197`, but the main
facade composition uses `IOrganizationService`.

The only performance-test candidate is not executable:
`CollectionQueryServiceAsyncTests.cs:36-41` leaves dependencies uninitialized,
and its "should not block" case at lines 293-319 has no blocking assertion or
test project.

### F03A-PERF-002 All-Column Defaults Amplify Network And Materialization Cost

Static inspection found 50 owned `ColumnSet(true)` occurrences. Representative
hotspots:

- Contact identity and lookup: `ContactService.cs:117-259`.
- Generic query service: `QueryService.cs:77-90`, `:115-127`, `:170-185`,
  `:211-225`.
- Collection query defaults: `CollectionQueryService.cs:94-144`,
  `:164-209`, `:216-344`, `:354-360`.
- Entity repository default: `EntityRepository.cs:135-149`.
- Attachment retrieval: `AttachmentService.cs:38-67`.
- Batch contact helper: `CrmAsyncExtensions.cs:201-211`.

Cost flow:

omitted projection/legacy helper -> CRM selects all attributes -> server and
SDK serialize them -> network transfers them -> `Entity.Attributes`
materializes them -> broad entity is retained or passed onward.

The cost is deterministic even without timing data. It grows with entity width,
row count, and binary fields. It also overlaps F03A-SEC-002 because extra PII
crosses the data boundary.

Counter-evidence:

- Some FetchXML methods explicitly enumerate attributes.
- Donation contact search uses `top='100'`.
- Activity party retrieval groups IDs by entity type and performs one query per
  type instead of an N+1 loop.

Therefore this issue is systemic defaults, not a claim that every F03A query is
unbounded or inefficient.

## N+1 And Batching Review

`MarketingListService.cs:170-224` and `:322-376` can issue one CRM request per
member via `Task.Run`. No current production consumer was found, and the
F03Q facade delegates list APIs to `ListService`, whose reachable methods use
`AddListMembersListRequest` or `ExecuteMultipleRequest` batches. The
MarketingListService candidate was rejected as a separate confirmed issue.

`ActivityService.cs:62-97` is positive counter-evidence: it groups party
references and retrieves each logical-name group in one query.

## Client Lifetime, Retry, And Resource Review

- `CrmConnectionPool` has a bounded semaphore, health validation, cleanup timer,
  removal of unhealthy connections, and disposal of clients/timer/semaphore.
  A pool leak finding was rejected.
- `ToolUtilityClass.Core.cs:149-205` disposes the facade, connection service,
  CRM clients, listener, writer, and stream when explicitly disposed.
- The static factory has no host-owned disposal path other than internal
  `ResetInstance`; this is a lifetime design concern, but process-lifetime
  retention alone does not prove recurring resource growth. It remains part of
  F03A-EXT-001 composition work.
- The main singleton client has no demonstrated retry/reconnect policy.
  Runtime failure frequency was not measured, so no separate retry performance
  issue is claimed.

## Repeated Materialization/Serialization

Batch/list methods commonly materialize chunks with `ToList` and convert each
batch with `ToArray`. These allocations are secondary to CRM network cost and
are bounded by input size. They do not justify a separate issue before query
projection and true async I/O are addressed.

## Required Performance Contracts

1. Native async client operations with propagated cancellation.
2. Explicit synchronous method names where native async is unavailable.
3. Required projections for generic query APIs.
4. Paging and attachment-size policies.
5. Batch operations that report partial failures accurately.
6. Load/thread measurements only after F01A/F01D establish an executable gate.
