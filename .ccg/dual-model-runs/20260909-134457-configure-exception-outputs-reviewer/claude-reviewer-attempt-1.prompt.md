ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: configure-exception-outputs

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.8.FixDuplicateName.Worktree

## Request
Read-only final review of independent exception outputs. User EXPLICITLY authorizes all four combinations of ExceptionNotifications:WriteExceptionLog and SendLine; BOTH=true flush before LINE, LINE-only never writes log, file-only no sender, off no outputs. Startup snapshot/default true. Review git diff plus NEW ToolUtility/Diagnostics/ExceptionOutputOptions.cs and ChurchReport.MemberInfo.Tests/LineSharedWorkflow/ExceptionOutputOptionsTests.cs. Relevant tracked files Program.cs, ToolUtility/Diagnostics/ExceptionDiagnostics.cs and docs/appsettings diff. Validate lifecycle/cancellation/dedup/error-status branches. Both Debug/Release focused tests 19/19 passed, builds passed. Builder/invalid config BEFORE diagnostics established intentionally fixed stderr only (settings cannot yet be known), documented. Do not expand scope to legacy unrelated catches. Do not change files, do not spawn agents, no more than 500 words. Return Critical/Warning/Info with precise evidence or PASS.

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