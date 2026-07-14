# F03A Runtime Validation Plan

Status: NO_RUNTIME_PENDING
Mode: DIAGNOSIS_ONLY

No retained F03A issue requires runtime execution to confirm the current
diagnosis. The hard-coded fallback, plaintext/full-row authentication,
synchronous I/O wrappers, all-column defaults, and mixed composition path are
directly visible in source.

## Current Prohibition

Do not run restore, build, test, package restore, generation, formatting,
migration, installers, or commands that write `bin/**`, `obj/**`, cache,
lockfiles, or test output. The known `net8.0` -> `net10.0` reference and missing
test-project enrollment block executable validation.

## Future Implementation Acceptance

These checks require a separately approved optimization task after F01A/F01D
repair the gate.

| Issue | Future check | Success threshold | Owner |
|---|---|---|---|
| F03A-SEC-001 | Missing-secret startup and secret scan | startup fails closed; no literal secret; rotated credential | F03A/X04A |
| F03A-SEC-002 | Authentication contract fixture | only identity/status returned; no password/full contact; invalid credentials indistinguishable | F03A/B01 |
| F03A-PERF-001 | Fake native-async client plus thread/load measurement | async SDK method invoked; cancellation propagated; no CRM `Task.Run` wrapper | F02/F03A/X02C |
| F03A-PERF-002 | Query-shape and payload measurement | exact projections/paging; no binary field unless requested; lower payload | F03A/B consumers |
| F03A-EXT-001 | DI and consumer gates | typed services resolve independently; F03Q compatibility and host compile remain green | F03A/F03Q/X01 |

## Safety

- Use non-production CRM fakes or an approved isolated environment.
- Never place real credentials, contact passwords, PII, or attachment data in
  fixtures/logs.
- Measure before and after with identical query/cardinality inputs.
- Roll out per API family and per business consumer.

## Runtime-Pending Count

Zero.
