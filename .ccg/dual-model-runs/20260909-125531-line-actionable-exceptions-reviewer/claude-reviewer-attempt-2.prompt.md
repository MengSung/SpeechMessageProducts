ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: line-actionable-exceptions

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.8.FixDuplicateName.Worktree

## Request
Review current git diff and new untracked exception files read-only (no edits). Main session inline implements. User requires all actionable failures logged to Logs/Exception.log in Debug AND Release; LOG MUST BE WRITTEN AND FLUSHED BEFORE LINE IS QUEUED/SENT. Permanent rules in AGENTS.md/spec. New ToolUtility/Diagnostics/ExceptionDiagnostics.cs and ExceptionReporting.cs; ChurchReport/Logging/ExceptionLoggerProvider.cs; Services/LineExceptionSender.cs; middleware; Program; BaseController; legacy admin facade; tests. Check lifecycle/deadlock/cancellation/recursive errors/sensitive state retention/log rotation across processes and logger configuration ordering. Inspect .ccg/tasks/line-actionable-exceptions/catch-audit.json for remaining terminal catch coverage gaps; recommend actionable vs recovered exclusions. Report Critical/Warning/Info with concrete file/line and remedy. Do not read configuration secrets. Provider quota fallback may apply. Current tests don't send actual LINE. Do not assume compilation implies correctness. Existing partial docs will be finalized to actual API after review.


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