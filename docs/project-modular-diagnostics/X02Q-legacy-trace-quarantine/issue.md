# X02Q Legacy Trace Quarantine Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: X02Q
Workspace: X02Q-legacy-trace-quarantine
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: QUARANTINE
Issue document SHA-256: b9afe25e73e32797fd23d32aa57eff0f82be2e5294f4d15c1ca851f8c7c99207

## Executive Summary

X02Q has three confirmed quarantine/governance findings. None proves an active
runtime vulnerability or production hot path. Direct optimization remains
prohibited until F01A selects a canonical project and a real consumer is proven.

## Ranked Confirmed Issues

### X02Q-EXT-001 Canonical Trace project is undecided

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 76
- Confirmed: true
- Evidence confidence: 18
- Impact score: 23
- Likelihood/frequency score: 14
- Security urgency score: 4
- Performance gain score: 3
- Loop leverage score: 10
- Ease/reversibility score: 4
- Effort: M
- Primary owner: X02Q
- Cross-module: F01A
- Gate blocked: true
- Files:
  - Trace/Trace.csproj:8
  - Trace/Trace_Fixed.csproj:8
  - Trace/Trace_Net10.csproj:8
  - Trace/Trace.csproj:83
  - Trace/Trace_Net10.csproj:78
- Evidence: three excluded project files declare the same `RootNamespace` and
  `AssemblyName` while compile definitions diverge and no canonical project is
  selected.
- Control/data/lifetime flow: duplicate project identities -> ambiguous build and
  ownership choice -> no canonical transfer target.
- Impact: no safe consumer proof, project inclusion, or X02B transfer can occur.
- Why this is necessary: quarantine must end in one explicit retain/transfer/retire
  decision rather than accidental revival.
- Recommended action: F01A selects exactly one canonical project or retires all;
  transfer to X02B only after consumer and test proof.
- Validation: solution/reference inventory shows exactly one eligible project or a
  documented complete retirement.
- Rollback boundary: `Trace/**` project and future solution membership only.
- Extraction contract: canonical project decision plus consumer/test evidence in;
  retained X02B provider or retirement record out.
- CCG round history:
  - Round 1: run `20260711-180600-x02q-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available; source rechecked true.

### X02Q-SEC-001 Historical signing key lacks a current ownership decision

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 61
- Confirmed: true
- Evidence confidence: 18
- Impact score: 16
- Likelihood/frequency score: 8
- Security urgency score: 12
- Performance gain score: 0
- Loop leverage score: 4
- Ease/reversibility score: 3
- Effort: S
- Primary owner: X02Q
- Cross-module: F01A
- Gate blocked: true
- Files:
  - Trace/Trace.csproj:23
  - Trace/Trace_Fixed.csproj:23
  - Trace/Trace_Net10.csproj:23
  - Trace/SpeechMessageCrmKey.snk:1
- Evidence: all three project variants enable signing and reference the same key,
  but no active owner, trust dependency, or retirement decision is recorded.
- Control/data/lifetime flow: signing-key references with unknown provenance ->
  ambiguous assembly trust if a quarantined project is revived.
- Impact: unknown provenance blocks safe revival; no active key compromise is
  claimed without a product consumer.
- Why this is necessary: build/signing governance must classify the key before
  project inclusion or removal.
- Recommended action: F01A records active, historical-only, or removable status and
  the corresponding retain/retire procedure.
- Validation: documented F01A decision and unchanged product runtime/assembly trust.
- Rollback boundary: key classification and Trace project inclusion; no key
  modification in diagnosis.
- Extraction contract: N/A.
- CCG round history:
  - Round 1: run `20260711-180600-x02q-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available; source rechecked true.

### X02Q-PERF-001 Optimization is blocked by missing runtime consumer proof

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 58
- Confirmed: true
- Evidence confidence: 18
- Impact score: 14
- Likelihood/frequency score: 8
- Security urgency score: 0
- Performance gain score: 6
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: X02Q
- Cross-module: X02B only after retention proof
- Gate blocked: true
- Files:
  - Trace/BSUStackTrace.cs:34
  - Trace/BSUTextWriterTraceListener.cs:31
  - Trace/BSUTextWriterTraceListener.cs:185
- Evidence: Trace implements stack/listener behavior, but product-consumer and
  solution-reference scans found no active runtime path.
- Control/data/lifetime flow: inactive implementations -> no consumer -> no
  measurable optimization path or baseline.
- Impact: optimizing quarantined code creates churn and obscures the retain/retire
  decision without production value.
- Why this is necessary: consumer proof is a hard prerequisite for runtime claims
  and X02B ownership transfer.
- Recommended action: keep quarantined until F01A retention and a concrete consumer
  are proven; otherwise retire.
- Validation: if retained, targeted listener/stack tests and measurement of the real
  X02B consumer path.
- Rollback boundary: no product module depends on `TraceNameSpace` before gates pass.
- Extraction contract: consumer and canonical project proof in; X02B runtime
  provider contract or retirement out.
- CCG round history:
  - Round 1: run `20260711-180600-x02q-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available; source rechecked true.

## Runtime Validation Pending

Runtime validation becomes applicable only if F01A retains a canonical project and
a real product consumer is identified.

## Deleted Or Rejected Candidates

- Direct Trace optimization, active runtime security, and active production
  performance claims are rejected while X02Q remains quarantine and no consumer is
  proven.

## Cross-Module Handoffs

- F01A decides project/signing retention. X02B accepts runtime observability
  ownership only after canonical project, consumer, and executable tests exist.

## Final CCG Approval

`DEGRADED_REVIEW_PENDING`; round 1 produced no usable backend output.
