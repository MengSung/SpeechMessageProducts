# B06C Runtime Validation Plan

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Gate Status

B06C is gate-blocked by the module map because B06A-B06C do not have directly attributable existing test suites. This plan defines the runtime proof needed before any optimization claim.

## Validation Items

### B06C-SEC-001 Plaintext register password storage

- Reproduction: submit a register request for an existing eligible contact in an isolated CRM/test double environment.
- Measurement: inspect the persisted credential field and authentication reader behavior.
- Expected pass condition: new or changed credentials are stored through a hash/verification contract, or the legacy raw field is explicitly quarantined with migration controls.
- Current status: static issue confirmed; no runtime validation run.

### B06C-SEC-002 Qualification posted LINE user id tampering

- Reproduction: authenticate as user A, then POST `GetQualificationData` and `SaveQualificationData` with user B's `UserLineId`.
- Measurement: response status, CRM read/write target, audit/log output, and whether B01 guard rejects the mismatch.
- Expected pass condition: mismatched posted LINE user id is rejected or ignored in favor of server-side trusted identity.
- Current status: runtime validation pending.

### B06C-SEC-003 CSRF / anti-forgery coverage for register and qualification POSTs

- Reproduction: submit register and qualification POSTs without a valid anti-forgery token under an authenticated browser session.
- Measurement: status code and absence of CRM mutation.
- Expected pass condition: request is rejected before CRM mutation, or a documented global filter proves equivalent protection.
- Current status: runtime validation pending.

### B06C-PERF-001 Register CRM call count and latency

- Reproduction: run register outcomes for successful registration, duplicate account, missing contact, mobile mismatch, and no qualifying list.
- Measurement: CRM calls per request, elapsed time, allocations if available, and retry/throttle behavior.
- Expected pass condition: baseline call counts are documented before extraction or batching work.
- Current status: runtime validation pending.

### B06C-PERF-002 Eligibility condition

- Reproduction: contact has matching name/mobile but no qualifying race/family leader list.
- Measurement: whether account/password fields are updated.
- Expected pass condition: behavior matches product rule; if no-list contacts should not register, write is blocked.
- Current status: static issue confirmed; behavior proof pending.

### B06C-EXT-003 Church hierarchy contract

- Reproduction: request `ListManagement.LoadChurchRoot` through the B06C-compatible route surface with representative hierarchy data.
- Measurement: payload shape, paging/sorting behavior, authorization, and consumer expectations.
- Expected pass condition: B06A provider and B06C consumer contract is documented and testable without small-group reporting ownership bleed.
- Current status: runtime validation pending.

## Prohibited During Validation Design

- Do not run restore, build, test, package restore, code generation, formatting, migrations, or product code writes as part of this diagnostic task.
- Future validation may define commands separately, but this workspace only records the plan.

## Bounded Validation Outcome - 2026-07-13

| ID | Result | Disposition |
|---|---|---|
| B06C-SEC-001 | Plaintext write exists, but `Home.ProcessRegister` route is absent | `STATIC_CONFIRMED_ORPHANED_PATH; NOT_RUNTIME_REACHABLE` |
| B06C-SEC-002 | Caller-supplied LINE ID reaches CRM read/write path; concrete `ToolUtilityClass` prevents safe interception | `BLOCKED_NO_TEST_SEAM_AND_EXTERNAL_CRM` |
| B06C-SEC-003 | Active qualification POST lacks anti-forgery; Register POST target is absent | `STATIC_CONFIRMED_QUALIFICATION_GAP; REGISTER_NOT_RUNTIME_REACHABLE` |
| B06C-PERF-001 | Register path is not reachable | `NOT_RUNTIME_REACHABLE` |
| B06C-PERF-002 | Always-true condition confirmed in orphaned path | `STATIC_CONFIRMED_ORPHANED_PATH` |
| B06C-EXT-003 | No fake `IInMemoryDataContext` or isolated hierarchy fixture | `RUNTIME_VALIDATION_PENDING_NO_ISOLATED_HIERARCHY_FIXTURE` |

No production or external CRM request was executed. B06C remains
`RUNTIME_VALIDATION_PENDING` because active identity and hierarchy proofs cannot
be measured safely with the existing seams.
