ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: configure-exception-outputs

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.8.FixDuplicateName.Worktree

## Request
Read-only concise analysis of adding independent ExceptionNotifications:WriteExceptionLog and SendLine booleans, default true, startup snapshot in existing Program and ExceptionDiagnostics. User explicitly authorizes LINE-only bypassing log. Both=true must flush before enqueue, off must cause no log/network, LINE errors when log off use fixed stderr only. Suggest minimal changes, risks/tests. Do not spawn tools/agents or modify files; inspect only Program.cs, ToolUtility/Diagnostics/ExceptionDiagnostics.cs as needed; output within 500 words.

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