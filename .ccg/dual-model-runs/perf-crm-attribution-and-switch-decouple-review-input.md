# Reviewer request: perf CRM attribution and SessionVerbose decoupling

Repository: D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree
Branch: feat/dataverse-scoped-connection

Review the current unstaged implementation against:
.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/handoff-codex.md
.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/prd.md
.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/design.md
.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/implement.md

Scope is Step 1 and Step 2 only. Do not propose changes to the analyzer, ensureMin,
CHURCH_REPORT_TRACE.TXT, or Line.Messaging/LineMessagingClient.cs CS1572.
Check DI lifetime/isolation/resource ownership, SessionVerbose semantics, tests,
Release #if DEBUG protection, UTF-8/CRLF documentation requirements, and whether
the collected Step 4 evidence is sufficient. Classify findings as Critical,
Warning, or Info with concrete file/line evidence. A missing authenticated CRM
trace must be reported as missing evidence, never estimated.
