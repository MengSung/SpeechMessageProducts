ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: p2-culture-timestamps

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
Active task: .trellis/tasks/11-07-p2-01-hosting
Read-only analysis; do not edit files, deploy, call external APIs, inspect appsettings, secrets, credentials, environment values or parent worktrees.
Scope: P2-01 culture completion and user-requested progress timestamps. Current HEAD a1dcc3cb. Existing new code is CultureCompletenessGenerator.cs + CommandRunner generate-completeness, production generated Localization/CultureCompleteness.g.cs (45 keys, en6), StartupGuards now rejects incomplete cultures in all environments. Tools179/179 and culture guard5/5 local tests passed. Pending config parsing and Program integration.
Analyze safe implementation for StartupGuards.ValidateLocalizationConfiguration(IConfiguration,bool): immutable options, bounded64 sequential array entries, reject scalar/nested/invalid shapes and booleans without echoing values; register returned options in Program before RunApplication, after diagnostics and ICU guard.
User new request: every P2 general-info history line needs date/time forever; keep one phase-2-summary. Preserve original records; use existing timestamp where known, label added historical update timestamp explicitly when original event time was never recorded. Add permanent AGENTS rule and meaningful progress test, no layout change. Identify precise failure cases. No source secrets in output. Return Critical/Warning/Info with file references. P2 full goal remains 0/12; not claim whole P2 completion.

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.