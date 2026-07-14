# X01 Host Composition Routes Lifetimes Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: X01
Workspace: X01-host-composition-routes-lifetimes
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: ddd5527314d69439acd2e57a813e406a3dedef79bf2f228232520c6992f3d41f

## Executive Summary

Static review confirms one ranked X01 design issue: the large legacy route table
has no stable route-contract snapshot. Middleware ordering, DI resolution,
Kestrel settings, and debug shutdown are hypotheses that remain under runtime
validation. The existing web-cache-deception test is a positive control, not an
issue.

## Ranked Confirmed Issues

### X01-PERF-003 Legacy route table lacks a stable compatibility snapshot

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 66
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 5
- Performance gain score: 3
- Loop leverage score: 5
- Ease/reversibility score: 3
- Effort: M
- Primary owner: X01
- Cross-module: all route consumers; callbacks include B05/F08/F09
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Startup.cs:878
  - SpeechMessageProducts.ChurchReport/Startup.cs:881
  - SpeechMessageProducts.ChurchReport/Startup.cs:1180
- Evidence: `Startup.Configure` registers more than fifty named legacy
  `routes.MapRoute` entries through `UseMvc`, while no generated route snapshot or
  executable compatibility baseline is recorded for X01.
- Control/data/lifetime flow: incoming path -> ordered legacy route table ->
  controller/action selection across every business and callback module.
- Impact: route cleanup, registration extraction, or endpoint-routing migration can
  silently break deep links, authentication, LINE, QR, and payment callbacks.
- Why this is necessary: X01 owns route compatibility, so a stable inventory is a
  prerequisite for any route or composition acceleration.
- Recommended action: generate and review a route snapshot containing name,
  template, controller, action, parameter shape, and representative smoke paths.
- Validation: compare the snapshot and execute representative paths listed in
  `evidence/runtime-validation-plan.md` before and after any route change.
- Rollback boundary: additive X01 route inventory/test tooling; no route behavior
  changes in the first increment.
- Extraction contract: host route registrations in; deterministic route-contract
  snapshot and compatibility result out.
- CCG round history:
  - Round 1: run `20260711-172500-x01-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

## Runtime Validation Pending

### X01-SEC-001 Middleware/session/authentication order needs a host smoke

- Confirmed: false
- Evidence: `Startup.cs:829-878` manually orders session, validation,
  authentication, identity audit, and legacy routing.
- Required validation: start a test-safe host and prove representative static,
  unauthenticated, and authenticated paths traverse the intended order.

### X01-SEC-002 Debug trace and GC monitoring need shutdown validation

- Confirmed: false
- Evidence: `Program.cs:79`, `Program.cs:232`, and `Program.cs:234` start an
  untracked debug-only task while the trace listener has an application-stopping
  cleanup path.
- Required validation: prove clean Debug shutdown and released trace handles before
  deciding whether a hosted-service remediation is needed.

### X01-PERF-001 DI singleton and hosted-service resolution needs a baseline

- Confirmed: false
- Evidence: host composition registers broad singleton and hosted-service graphs,
  but no test-safe resolution/start-stop command is recorded.
- Required validation: resolve key services with fake external dependencies and
  detect missing/captive registrations.

### X01-PERF-002 Kestrel and response-pipeline settings need load evidence

- Confirmed: false
- Evidence: `Program.cs:50-51` sets a 30-minute header timeout and null request
  buffer limit while startup configures compression, static files, cache, session,
  and authentication.
- Required validation: capture startup, memory, first-request, and representative
  static/dynamic load metrics before changing values.

## Deleted Or Rejected Candidates

- X01-SEC-003 web-cache-deception guard: positive control supported by
  `StaticRequestPathHelperTests`; include it in the future baseline but do not rank
  it as a defect.
- X01-EXT-001 registration extension groups: defer until DI and host baselines
  exist; extraction alone is not a confirmed defect.
- X01-EXT-003 debug hosted-service extraction: defer unless X01-SEC-002 proves a
  shutdown or lifetime failure.

## Cross-Module Handoffs

- X01-EXT-002 route inventory remains X01-owned and is the remediation contract for
  X01-PERF-003; it is not duplicated as a second ranked issue.
- Business implementations remain in their owner modules even when X01 registers
  them.

## Final CCG Approval

`DEGRADED_REVIEW_PENDING`; round 1 produced no usable backend output.
