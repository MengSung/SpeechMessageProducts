# B04A Performance Analysis

Final status: DEGRADED_REVIEW_PENDING

## Hot Paths

### P1. Upload creates and updates present records one member at a time

- Evidence: `CreatePresentRecordList` loops through `aSmallGroupData.Members`, calls `CreatePresentRecord`, assigns owner, and appends the entity for each member.
- Evidence: create/update helpers call CRM operations such as create, retrieve, assign owner, update, and contact retrieval inside per-member flows.
- Impact: request time grows with member count and CRM round trips. This can throttle or partially complete under CRM latency.
- Optimization: prefetch contacts and existing present records by GUID, compute a command plan in memory, then perform batched or grouped CRM writes with per-record results.

### P2. Validation performs CRM retrievals inside loops

- Evidence: `GetValidMemberNumber` loops present records and calls `IsValidMember`.
- Evidence: `IsValidMember` retrieves the contact for each present record and may update contact state.
- Impact: validation behaves like N+1 IO and can mutate contacts during a count/validation pass.
- Optimization: separate pure validation from repair/update behavior. Prefetch all contacts referenced by present records once.

### P3. List membership and contact matching scan collections and retrieve contacts one by one

- Evidence: `UpdateContactInfomationFromList` retrieves the list type, retrieves members, loops each member, resolves contact, checks active state, and compares `fullname`.
- Evidence: upload-side list update logic similarly retrieves member list collections and contact entities inside loops.
- Impact: repeated list/contact scans create unnecessary CRM load and are vulnerable to duplicate-name ambiguity.
- Optimization: create a request-scoped `ListMembershipSnapshot` keyed by contact GUID and present record GUID.

### P4. Thread-pool parallelism hides synchronous in-memory mutation

- Evidence: `UpdateSmallGroupPresentRecord` wraps two `UpdateMember` calls in `Task.Run` and awaits both.
- Impact: this consumes thread-pool work while mutating related state in parallel. It does not reduce CRM round trips and can produce race conditions.
- Optimization: use one synchronous domain update over both projections, or a controlled transaction/lock if the state must remain mutable.

## Performance Verdict

The highest leverage optimization is not micro-tuning. It is introducing a B04A query/write boundary that batches CRM IO, makes validation pure where possible, and updates request-visible attendance state atomically.
