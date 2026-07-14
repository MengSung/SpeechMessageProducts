# X02C Performance Profiling Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: X02C
Workspace: X02C-performance-profiling
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 097bfeb0f0dde1e79943839c63a67b1b40bba4d753c84f5dfee9aba5f4ecb3b8

Nested agent count: 0

## Executive Summary

X02C owns request/startup profiling, timing filter/middleware, thresholds, the performance parser/monitor, and profiling signals. Static review keeps one confirmed security/design issue for the DEBUG performance API surface. The performance-monitor unbounded-retention candidate was rejected after re-reading PerformanceMonitor.cs:85-89, which caps each metric list at 1000 samples. No product code change is proposed in this diagnostic workspace.

## Scope Summary

Primary owner files reviewed are listed in evidence/scope-manifest.md. Explicit exclusions are cache correctness, logging provider internals, business KPI/performance decisions, logging provider implementation, product optimization, generated files, tests, build outputs, and ledger edits.

## Ranked Confirmed Issues

### X02C-SEC-001 DEBUG performance endpoints expose operational profiling and reset controls without an explicit local/admin guard

- Category: Security
- Severity: Medium
- Priority: P2
- Priority score: 63
- Confirmed: true
- Evidence confidence: 15
- Impact score: 15
- Likelihood/frequency score: 8
- Security urgency score: 12
- Performance gain score: 0
- Loop leverage score: 8
- Ease/reversibility score: 5
- Effort: S
- Primary owner: X02C
- Cross-module: false
- Gate blocked: false
- Files:
  - SpeechMessageProducts.ChurchReport/Controllers/PerformanceController.cs:28
  - SpeechMessageProducts.ChurchReport/Controllers/PerformanceController.cs:50
  - SpeechMessageProducts.ChurchReport/Controllers/PerformanceController.cs:70
  - SpeechMessageProducts.ChurchReport/Controllers/PerformanceController.cs:103
  - SpeechMessageProducts.ChurchReport/Controllers/PerformanceController.cs:139
  - SpeechMessageProducts.ChurchReport/Startup.cs:284
- Evidence: PerformanceController is DEBUG-only but exposes report, session statistics, target validation, summary, and reset endpoints without an explicit action-level authorization, environment/localhost guard, anti-forgery requirement for reset, or dependency on ProfilingSwitch.Enabled. Startup.cs registers IPerformanceMonitor in DEBUG at line 284.
- Control/data/lifetime flow: HTTP requests to /api/performance/* reach the controller in DEBUG builds, read monitor/session operational state, and POST reset mutates in-memory performance counters. Session monitor internals are X02B context.
- Impact: If a DEBUG build or diagnostic instance is reachable outside a trusted local operator boundary, the endpoints disclose operational metrics and allow resetting performance evidence. This is not a Release production finding because the controller is DEBUG-only.
- Why this is necessary: X02C already has a runtime gate for request profiling, but the performance controller does not reuse a comparable access boundary.
- Recommended action: Add an explicit local/admin/debug-diagnostic guard for X02C performance endpoints and require reset to be protected by the same guard.
- Validation: In DEBUG, verify unauthenticated/non-local requests cannot call /api/performance/report, /summary, /validate, /sessions, or /reset; verify authorized/local diagnostic use still works.
- Rollback boundary: Revert only the controller guard/registration change; no product business behavior should change.
- Extraction contract: N/A
- CCG round history:
  - Round 1: run `20260711-175507-x02c-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available; source rechecked true

## Ranked Security Issues

1. X02C-SEC-001 Medium/P2: DEBUG performance endpoints need an explicit local/admin diagnostic guard.

## Ranked Performance/Design Issues

No confirmed performance/design issue. The original unbounded-retention candidate was rejected because PerformanceMonitor.cs:85-89 caps each metric list at 1000 samples.

## Ranked Extraction/Acceleration Issues

No confirmed extraction/acceleration issue. A possible profiling-signal contract remains future context only, not an approved issue.

## Runtime Validation Pending

No confirmed issue currently requires runtime validation to determine KEEP/DELETE.

## Deleted Or Rejected Candidates

- Performance monitor unbounded sample retention: rejected. PerformanceMonitor.cs:85-89 caps each metric list at 1000 samples by removing the oldest value.
- Request profiling middleware path PII leak: rejected. PerfProfilingMiddleware prefers route templates and sanitizes GUID/numeric path segments before writing [Perf] lines.
- Startup profiler session leakage: rejected. StartupProfiler is DEBUG-only and records phase labels and elapsed milliseconds during startup, before request/session context exists.
- CRM timing wrapper production overhead: rejected because the reviewed profiling classes are compiled under #if DEBUG and additionally gated by ProfilingSwitch.Enabled.
- Cache performance monitor correctness: rejected from X02C scope because cache correctness and cache-specific monitor behavior belong outside this leaf except as dependency/consumer context.

## CCG Outcome Summary

Round 1 input: .ccg/dual-model-runs/x02c-issue-review-r1-input.md.

Current outcome: CCG review is DEGRADED_REVIEW_PENDING: summary shows no completed backends, failed backends include gemini and claude, degradedFallback=false, quotaBlocked=true, fallbackAccepted=true. No backend produced usable output, so degraded approval is not available.



## CCG Round 1 Result

- Runner exit code: 3
- Run directory: D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion\.ccg\dual-model-runs\20260711-175507-x02c-issue-review-r1-reviewer
- Summary path: D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion\.ccg\dual-model-runs\20260711-175507-x02c-issue-review-r1-reviewer\summary.json
- Usable backend output: False
- Final status: DEGRADED_REVIEW_PENDING
- CCG result: summary=D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion\.ccg\dual-model-runs\20260711-175507-x02c-issue-review-r1-reviewer\summary.json ok=False degradedFallback=False quotaBlocked=True fallbackAccepted=True


## Status Correction

- Corrected final status: DEGRADED_REVIEW_PENDING
- Completed backends: none / empty
- Failed backends: gemini, claude
- degradedFallback: false
- quotaBlocked: true
- fallbackAccepted: true
- Usable backend output: false
- Decision: no backend produced usable output, so degraded approval is not available and completed-backend findings cannot be applied.
