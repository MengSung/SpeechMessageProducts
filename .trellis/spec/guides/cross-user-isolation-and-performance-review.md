# Cross-User Isolation and Performance Review

Use this checklist before designing, implementing, reviewing, or approving any
change in any current or future product line. The full executable contract is
[Cross-User Isolation and Sustainable Performance](../backend/cross-user-isolation-and-performance.md).

## Non-Negotiable Question

Can authenticated subject A ever see, reuse, mutate, infer from an error, or
retain any state/data belonging to subject B? If the answer is not a proven
"no", stop and design the isolation boundary before continuing.

## Before Writing

- [ ] Identify the complete server-validated isolation boundary: subject or
  workload, tenant, product, authorization scope, and any selected profile /
  runtime generation.
- [ ] Trace every data path: request -> authorization -> cache -> service ->
  connector/store -> response/UI/log/background work.
- [ ] Identify the one owner and maximum lifetime for every client, lease,
  permit, buffer, stream, timer, cancellation registration, queue entry,
  temporary file, process, and cache entry.
- [ ] Decide which data is globally safe immutable metadata and which data is
  user-/tenant-/profile-specific. Do not use a shared cache for the latter
  unless its full validated partition, bounds, and eviction are explicit.
- [ ] Choose bounded pooling/back-pressure/pagination instead of a global lock,
  an unbounded scan, or a fresh expensive runtime per ordinary request.

## During Implementation

- [ ] Do not place request, session, principal, tenant, profile, credential,
  token, cookie, response DTO, CRM entity, authorization result, or mutable
  collection in static/shared state.
- [ ] Do not accept caller-controlled identity, tenant, profile, endpoint,
  credential, connector, or organization as authority.
- [ ] On cancellation, timeout, fault, or drain, evict uncertain clients and
  release all permits/resources in deterministic reverse ownership order.
- [ ] Prevent raw upstream errors, cache entries, log payloads, browser state,
  and IPC data from exposing another subject's information.

## Before Approval

- [ ] Run interleaved/concurrent A/B tests with distinct synthetic markers and
  assert no cross-response, cross-cache, cross-log, or cross-UI visibility.
- [ ] Inject fault/cancellation/cleanup paths and assert resources return to
  baseline without a faulted client re-entering a reusable pool.
- [ ] Run a bounded soak/lifecycle test when the change owns pooled or
  long-lived resources.
- [ ] Verify the normal path has no major sustained performance regression.
  Small fixed costs that enforce isolation are required and acceptable.
