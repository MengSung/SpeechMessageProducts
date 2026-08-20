# CCG reviewer Task: perf-crm-attribution-and-switch-decouple

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
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


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.