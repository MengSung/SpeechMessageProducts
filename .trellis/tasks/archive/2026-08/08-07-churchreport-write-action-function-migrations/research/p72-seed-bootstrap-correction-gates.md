# Research: P7.2 seed bootstrap correction gates

- Query: Identify the explicit acceptance criteria and required documentation, review, and safety gates for correcting the P7.2 Slice C seed/bootstrap prerequisite after deterministic cleanup removed the current-user source/graph descriptor pair.
- Scope: internal
- Date: 2026-08-11

## Findings

### Current state and scope boundary

The one authorized independent-cycle `FreshPreflightProbe` returned the sanitized `no-go / fixture-input-required` result before Credential Manager access, child-process creation, or CE I/O.  The descriptor pair was intentionally removed by prior exact-ID cleanup and the retained backup must not be revived as a stale baseline.  The existing cycle is permanently non-retryable; Slice D--H remain closed.  A correction is therefore a separately planned local control-plane/bootstrap capability, not permission to re-run a probe, provision, execute, reconcile, clean up, or mutate a weekly report.  See `.ccg/tasks/p7-2-churchreport-write-action-function-migrations/task.json:9-10` and `.trellis/tasks/08-07-churchreport-write-action-function-migrations/p7.2-slice-c-continuation-2026-08-10.md:452-475`.

### Explicit acceptance criteria for a safe bootstrap

1. **Fail closed before external boundaries.** Missing, malformed, wrong-owner, stale, or incomplete source/graph input must yield a bounded no-go with zero credential access, child process, ledger creation, descriptor publication, or CE I/O.  The current parent already checks both descriptor paths before credential handling (`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1:2494-2499`); a bootstrap must preserve that ordering.
2. **No stale or caller-selected targets.** Never repair, update, associate, delete, re-publish, or infer identifiers from the historical source contact or expected relationship list.  Operational static lists are read-only inputs (`p7.2-slice-c-fresh-fixture-provisioning-design.md:30-32,154-156`).  Do not scan/select a user, accept a caller-provided owner, or weaken same-owner assignment.
3. **Prove the complete server-validated precondition first.** Bind to the deployment-owned `crm91`/Data8 CE 9.1 profile, current Windows owner, task marker, WhoAmI identity, active non-service baseline owner different from the Data8 service user, exact static-list graph, and fixed UTC-Sunday target-list weekly-report query.  The existing provisioner rejects invalid request/list/owner proof before mutation (`P72FreshSliceCFixtureProvisioner.cs:307-326`).
4. **Preserve weekly-report semantics.** `zero-active` is a successful unlinked-present-record branch; `exactly-one-active` must use and read back that exact lookup; `duplicate-active`, paging, malformed, or unavailable data is a zero-mutation no-go.  It must never create, repair, deactivate, delete, or select a weekly report by name (`p7.2-slice-c-fresh-fixture-provisioning-design.md:151-153,220-225`).
5. **Use only the fixed fresh-graph mutation allowlist after proof.** At most three fixed Creates (source contact, leader contact, relationship list), exactly two fixed `AddListMembersListRequest` calls, and one fixed `AssignRequest`; every successful stage requires exact read-back before the next stage.  The existing sequence implements this shape at `P72FreshSliceCFixtureProvisioner.cs:329-405`; arbitrary entity, field-map, endpoint, connector, and credential input remain forbidden.
6. **Make ambiguity recoverable but never retryable.** Persist one current-user, non-reparse, bounded pending ledger before/after the defined stages; a timeout, transport fault, ledger error, read-back failure, or non-zero child exit is no-go, does not retry or guess IDs, does not publish descriptors, and retains recovery data only for exact-ID reconciliation or an explicitly authorized cleanup lane (`p7.2-slice-c-fresh-fixture-provisioning-design.md:185-199`).
7. **Publish atomically only after final graph proof.** Publish both fresh descriptors only after all final graph reads succeed and the child exits successfully.  Publication failure quarantines the pair, retains the ledger, and must never restore stale descriptor bytes (`p7.2-slice-c-fresh-fixture-provisioning-design.md:201-206`).
8. **Cleanup is exact-ID and reverse ordered.** Cleanup must re-prove every ledger entity by logical name and marker; remove memberships with read-back, delete fresh relationship list, then source/leader contacts; remove descriptors only after all remote absence read-backs.  Any uncertain cleanup blocks further writes and retains the ledger (`p7.2-slice-c-fresh-fixture-provisioning-design.md:208-214`).

### Required documentation and review gates

- Every substantively changed `.cs` region must have complete Traditional Chinese XML documentation for types, constructors, methods, important properties, fault behavior, ownership, isolation, bounded lifetime, cancellation/timeout, and deterministic cleanup.  PowerShell contract comments must likewise explain authorization/credential/process/evidence ownership.  Existing source sets this standard in `P72FreshSliceCFixtureProvisioner.cs:1-16,232-240` and `Invoke-Package02Data8ListManagementEvidence.ps1:2190-2200`.
- Update the task-owned PRD/design/implementation/continuation and CCG record with the separate bootstrap decision, exact mutation boundary, no-go taxonomy, verification results, and any external review status.  Do not edit specs from this research task; use the update-spec workflow only if a reusable engineering contract is learned.
- Treat the prior independent-cycle CCG review as **not completed**: Gemini timed out and Claude was quota/session blocked, so its partial output is unusable and it is not an accepted single-model fallback (`p7.2-slice-c-continuation-2026-08-10.md:469-472`; `.ccg/tasks/.../review.md:161-173`).  The bootstrap correction needs a new self-healing CCG dual-model analysis and review.  If a future backend is quota-blocked, record degraded/incomplete status exactly as the CCG guide requires; never claim a successful dual review without two usable outputs.

### Required verification and safety gates

- Start with failing tests that prove: stale IDs receive zero mutations; each missing/invalid precondition produces zero mutation; `zero-active` remains valid; duplicate/unavailable weekly reports fail before mutation; each fixed request has the exact shape/read-back; ambiguity prevents publication and retains only the bounded ledger; cleanup cannot target non-ledger data.  This is the required TDD list in `p7.2-slice-c-fresh-fixture-provisioning-design.md:216-225`.
- Run the focused C# provisioner/preflight/live-gate tests, strict PowerShell fresh-fixture contract suite, P7.2 coverage-validator tests, Release build, serial solution tests, `git diff --check`, and byte-level UTF-8-no-BOM/CRLF/final-CRLF validation.  The coverage validator is expected to remain no-go for the four Slice C live-evidence rows until separately authorized real CE evidence exists; this is not a local-code pass condition.
- Add/retain concurrent A/B current-user/profile isolation tests plus fault, cancellation, child-timeout, process-environment restoration, temporary-directory cleanup, ledger cleanup, and resource-baseline/soak assertions.  The parent must restore environment and dispose process in `finally` (`Invoke-Package02Data8ListManagementEvidence.ps1:3017-3047`); no credential, descriptor ID, endpoint, CRM row, raw exception, token, cookie, or ledger content may enter console JSON, TRX, logs, shared state, or evidence.
- No Dynamics action was performed for this research.  Even after local gates are green, any CE provision/probe/cleanup requires a separate explicit authorization and must stop at the first no-go, ambiguity, read-back mismatch, or cleanup uncertainty.

## Files Found

- `.ccg/tasks/p7-2-churchreport-write-action-function-migrations/task.json` — authoritative CCG blocked-state and next-action wording.
- `.ccg/tasks/p7-2-churchreport-write-action-function-migrations/review.md` — prior CCG/review status, local checks, and incomplete independent-cycle review record.
- `.trellis/tasks/08-07-churchreport-write-action-function-migrations/p7.2-slice-c-continuation-2026-08-10.md` — chronological evidence, current no-go, and explicit safe-bootstrap prerequisite.
- `.trellis/tasks/08-07-churchreport-write-action-function-migrations/p7.2-slice-c-fresh-fixture-provisioning-design.md` — approved fixed graph, mutation allowlist, ledger/publication/cleanup contract, and test-first requirements.
- `.trellis/tasks/08-07-churchreport-write-action-function-migrations/p7.2-fixture-activation-matrix.json` — Slice C activation ownership, allowed mutations, cleanup, reconciliation, and CE-evidence state.
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs` — current bounded fresh-graph preflight, mutation/read-back, and ledger ownership pattern.
- `ChurchReport.MemberInfo.Tests/P72FreshSliceCFixturePreflightProbe.cs` — read-only fresh preflight and weekly-report cardinality classification code.
- `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1` — PowerShell authorization, descriptor trust boundary, strict evidence parser, process/environment lifetime, and deterministic cleanup owner.
- `.trellis/spec/backend/cross-user-isolation-and-performance.md` — repository-wide isolation, lifecycle, and sustainable-performance contract.
- `.trellis/spec/guides/cross-user-isolation-and-performance-review.md` — mandatory A/B isolation, fault/cleanup, and bounded-soak review checklist.
- `.trellis/spec/guides/ccg-external-review-thinking-guide.md` — required self-healing dual-model review and quota fallback classification.

## External References

- No external documentation was consulted; this conclusion is based on the active Trellis/CCG records, local source, and repository specifications.

## Caveats / Not Found

- “Seed bootstrap correction” is not an existing named implementation symbol; it is inferred from the CCG task’s explicit alternative of a “separately planned and verified safe bootstrap capability.”
- The current task pointer is unavailable in this sub-agent session (`task.py current --source` returned none), but the parent supplied the explicit active task path above.
- The prior independent-cycle external review is incomplete, not evidence of approval.  This note intentionally authorizes no Dynamics/CE action.
