# B06A List Reference Data Diagnostic Issues

Status: RUNTIME_VALIDATION_PENDING
Module: B06A
Workspace: B06A-list-reference-data
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: dfc1417676c397fc0743b27e4c17bf5347308d8c0e572d5f0693c9bf36224f1b

## Executive Summary

B06A has three confirmed extraction/governance issues. Static convergence proved
that `IListManagementService` has no implementation or registration while B02
consumes it. Security, cache, metadata, and hot-path observations remain pending
because no targeted route/cache/call-count seam or isolated CRM fixture exists.

## Ranked Confirmed Issues

### B06A-EXT-001 Narrow reference and list provider contract is missing

- Category: Extraction
- Severity: High
- Priority: P2
- Priority score: 67
- Confirmed: true
- Evidence confidence: 18
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 2
- Performance gain score: 4
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: B06A
- Cross-module: B02, B05, B06B, B06C
- Gate blocked: true
- Files:
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:749
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:809
  - SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:44
  - SpeechMessageProducts.ChurchReport/Services/ListManagement/IListManagementService.cs:25
- Evidence: the map declares B06A as provider to B05/B06B/B06C, while B02 injects
  the list interface; no complete caller/DTO/query contract is recorded.
- Control/data/lifetime flow: B06A list/reference API -> B02/B05/B06B/B06C
  consumers -> module-specific CRM and presentation behavior.
- Impact: undocumented coupling forces cross-module edits and prevents independent
  provider/consumer gates.
- Why this is necessary: one stable provider contract is required before any B06A
  extraction or consumer optimization.
- Recommended action: inventory all callers and define narrow immutable reference
  DTO and query operations.
- Validation: caller map plus B06A provider and four consumer compile/contract tests.
- Rollback boundary: add adapters and migrate one consumer at a time.
- Extraction contract: list/reference query commands in; immutable reference DTOs
  out; F03A dependency and fake CRM seam.
- CCG round history:
  - Round 1: Claude identified the missing B02 consumer context; Gemini was quota
    blocked; source rechecked true.

### B06A-EXT-004 IListManagementService is unimplemented and unregistered

- Category: Extraction
- Severity: High
- Priority: P2
- Priority score: 64
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 8
- Security urgency score: 0
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: B06A
- Cross-module: B02, X01
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Services/ListManagement/IListManagementService.cs:25
  - SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:37
  - SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:44
  - SpeechMessageProducts.ChurchReport/Startup.cs:376
- Evidence: a complete source/DI search found zero implementations and zero host
  registrations, while B02 `ContactService` requires the interface.
- Control/data/lifetime flow: future host DI resolution -> `ContactService`
  construction -> unresolved B06A dependency.
- Impact: the seam is dead while unreachable and becomes a deterministic DI failure
  if the consumer is activated.
- Why this is necessary: unresolved implementation and ownership block both B02 and
  B06A extraction gates.
- Recommended action: explicitly remove/transfer the dead dependency or implement
  and register the B06A contract.
- Validation: implementation/registration search and test-safe host DI-resolution
  smoke.
- Rollback boundary: one service registration and B02 constructor boundary.
- Extraction contract: existing list operations implemented through F03A with a
  fake list-store seam.
- CCG round history:
  - Round 1: Claude raised the static gap; Gemini was quota blocked; bounded source
    search confirmed `STATIC_CONFIRMED_UNREGISTERED_AND_CURRENTLY_UNREACHABLE`.

### B06A-EXT-003 MapData ownership lacks local executable proof

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 50
- Confirmed: true
- Evidence confidence: 20
- Impact score: 10
- Likelihood/frequency score: 8
- Security urgency score: 0
- Performance gain score: 0
- Loop leverage score: 7
- Ease/reversibility score: 5
- Effort: S
- Primary owner: B06A
- Cross-module: false
- Gate blocked: true
- Files:
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:435
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:436
  - docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:1022
- Evidence: the authoritative map corrected `MapData` and `MapDataList` to unique
  B06A ownership, but no caller/contract check preserves that correction.
- Control/data/lifetime flow: ownership map -> future caller classification and file
  movement decisions.
- Impact: later work can incorrectly transfer hierarchy/reference models into fee
  or register leaves.
- Why this is necessary: a durable local proof prevents ownership drift during
  modular extraction.
- Recommended action: add manifest references and a caller/contract boundary check.
- Validation: caller inventory proves no B06B fee or B06C register policy is owned
  by these types.
- Rollback boundary: documentation and boundary-test artifacts only.
- Extraction contract: B06A map/list DTO provider with consumer contract tests.
- CCG round history:
  - Round 1: no per-issue verdict was recorded; Gemini was quota blocked; source
    rechecked true.

## Runtime Validation Pending

- B06A-SEC-001 metadata/list endpoint authorization proof.
- B06A-SEC-002 option metadata exposure and field allowlist.
- B06A-SEC-003 identity-bearing list data redaction.
- B06A-PERF-001 metadata/cache call count and lifetime.
- B06A-PERF-002 list/reference materialization and query shape.

These remain unconfirmed until the route/cache/call-count seams in
`evidence/runtime-validation-plan.md` exist.

## Deleted Or Rejected Candidates

- B06A-PERF-003 and B06A-EXT-002 lack caller-level code proof; they require static
  investigation rather than being ranked or described as runtime measurements.

## Cross-Module Handoffs

- B02 currently consumes `IListManagementService`; B05/B06B/B06C are declared map
  consumers. X01 owns DI composition and F03A owns generic CRM operations.

## Final CCG Approval

`RUNTIME_VALIDATION_PENDING`; degraded Claude review plus bounded static checks do
not satisfy the missing runtime/provider/consumer gates.
