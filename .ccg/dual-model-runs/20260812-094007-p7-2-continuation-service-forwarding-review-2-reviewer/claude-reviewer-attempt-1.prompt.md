ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p7-2-continuation-service-forwarding-review-2

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
ROLE: reviewer
TASK: Review only the current P7.2 continuation diff for service ownership, cross-user/session isolation, deterministic resource lifecycle, and local-only CE fail-closed boundaries. Focus on ToolUtility/Core/ToolUtilityFacade.cs, ChurchReport.MemberInfo.Tests/WebServiceConnector/DownloadListManagerIsolationTests.cs, SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs, and SpeechMessage.Dynamics.Tests/P72ContinuationOperationIdsTests.cs. Do not connect to CRM, mutate CE, modify files, or output secrets/raw IDs. Return Critical/Warning/Info findings only. Verify that dynamic-list overloads preserve caller-provided operation service and that token/organization/profile input names are rejected. Also identify whether DownloadIntegrateData Factory ToolUtility remains a P7.4/P7.5 blocker.
OUTPUT: concise sanitized review report

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