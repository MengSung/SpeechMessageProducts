# CCG analyzer Task: p1-01-closeout-analysis

## Repository
C:\Users\Administrator\.codex\worktrees\p1-01-closeout\1.0.0.11.Integration.P1.Worktree

## Request
Read-only analysis of P1-01 orphan cleanup closeout. Do not modify files or run builds. Scope is validation and documentation, not product changes. Main agent owns all execution.
Formal 02 P1-01: remove Trace and LinePayCSharp only after no active references; delete two redundant _Net10 csproj; remove four Crm82/91Worker dirs only if lock-only; move 15 orphan performance tests into ChurchReport.MemberInfo.Tests/Performance with Performance trait; add ToolUtility.Tests to solution; before/after duplicate scan, build and tests; report search evidence. Already committed: f5c69cde deletes 51 files and moves test; 82515aab integrates tests/solution; 55e7caa1 adds concurrent fixture regression. Existing audit wp-reports/P1-01-reference-audit.md lists 51 files; no acceptance report yet. Fixed verification commit 03c8aa28 (P1-04 Activity work remains separately active in main checkout).
00 section8: full Release build NU1605-as-error, full Release test, Debug FileToolUtilityTracerTests, Changed encoding Base73b6e6cf, duplicates baseline, I18nCheck verify, architecture and net462 plugin packaging. Use offline tests only, no D365/LINE.
USER DECISION 2026-11-05: keep current DevExpress/DevExtreme version and functionality, no upgrade or subscription renewal, no UI replacement. Defer new package source and hosted full CI restoration; do not block P1-01 on these, never claim hosted CI passed. Local full solution validation still mandatory. Existing user package cache may be used without modifying global config or printing credentials.
Provide concise verification gap/checklist, warn of unsound completion claims, test preservation audit and safe parallel-work isolation. Existing history says baseline1125 tests +63 ToolUtility outside solution; latest baseline2278 in16 targets and16 performance tests. Need fresh checks. Avoid unrelated product scope creep.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
