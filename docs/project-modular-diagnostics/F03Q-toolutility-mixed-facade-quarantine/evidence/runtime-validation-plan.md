# F03Q Runtime Validation Plan

Status: COMPLETE_NO_RUNTIME_PENDING
Mode: DIAGNOSIS_ONLY

## Current Decision

No issue requires runtime evidence to establish its current diagnosis:

- SEC-001 is a source disclosure.
- EXT-001 is established by fields, dependencies, methods, consumers, and the
  authoritative quarantine rule.
- EXT-002 is established by source-level constructor/fake/behavior mismatch.
- PERF-001 is a conditional public-API lifetime defect established by mutation,
  lazy replacement, disposal, and synchronization paths. Low current usage is
  reflected in its likelihood score.

Therefore the issue document has no `NEEDS_RUNTIME_VALIDATION` item and must
not use `RUNTIME_VALIDATION_PENDING`.

## Prohibited Commands For This Diagnosis And CCG Review

Neither the Diagnostic Subagent nor CCG reviewers may run:

- `dotnet restore`, `dotnet build`, or `dotnet test`;
- package restore/install/update operations;
- code generation;
- formatting;
- migrations;
- any command that creates or changes `bin/**`, `obj/**`, cache, lockfile,
  coverage, TestResults, snapshot, generated, or other test output.

Read-only source reopening and searches are permitted.

## Future Validation Contracts After Separate Approval

### SEC-001

- Executor: F03Q/F03A/X04A owner tasks.
- Method: repository secret scan and exact-value search after rotation/removal.
- Success: no current-source credential value; startup secret validation owned
  by X04A/F03A.
- Failure effect: issue remains KEEP and blocks secret-remediation completion.

### EXT-001

- Executor: F03A/F03B/F01D/X01 integration tasks.
- Method: owner-specific DI/contract tests and consumer compile matrix.
- Success: CRM consumers resolve without LINE dependency; LINE audit adapter
  resolves without full CRM facade; F03Q contains forwarding only.
- Failure effect: roll back the affected method-family migration.

### EXT-002

- Executor: F01D then F03A/F03B/F03Q.
- Method: repair target/solution gate, compile owner-specific tests, and prove
  F03Q tests cover compatibility routing rather than alternate product behavior.
- Success: constructor binds to the real dependency contract and production
  LINE audit behavior has an F03B-owned test.
- Failure effect: keep old test documented but non-authoritative; do not delete
  product compatibility.

### PERF-001

- Executor: F03A/F02/X01 after API ownership approval.
- Method: disposable fake clients, deterministic barriers, old/new connection
  switch, call/disposal counters.
- Success: exactly one owner disposes each client, no stale call after switch,
  and concurrent behavior is deterministic.
- Failure effect: retain/deprecate the compatibility API and do not migrate
  consumers to connection switching.

## Safety Limits

- Use fake clients only; no production CRM or LINE credential.
- No outbound CRM/LINE call.
- No real user ID, message body, or contact data.
- Runtime work must occur in a separately authorized task with explicit output
  directories and rollback points.
