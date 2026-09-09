ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: line-actionable-exceptions

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.8.FixDuplicateName.Worktree

## Request
Analyze only, do not edit. User requests all actionable/unhandled/feature-impacting exceptions sent to existing administrator LINE recipient; explicitly exclude normal cancellation and recovered retries. Current ChurchReport BaseChurchController.HandleError calls blocking static ChurchReportLineAdminNotificationService, some catches only Trace/Debug, other services log ILogger Error. Inspect relevant sources and propose safe coverage approach. Design intended: shared bounded asynchronous alert dispatcher (no request/exception references retained), safe metadata only (type, source/stack symbol names, generated incident ID/time; exclude raw message/path/query/session/credentials), HTTP middleware INSIDE standard exception handler, ILogger provider Error/Critical bridge, explicit catch reporting for swallowed feature failures. Preserve original error behavior, no FirstChanceException noise. Bounded batch/queue, timeout/cancellation, deterministic stop/dispose and no recursive LINE failure notification. Find major coverage gaps and practical source/test paths. Main session implements inline. Output Critical/Warning/Info plus concrete recommendations. Avoid reading appsettings secret values.


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