ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p74-ungrouped-commitment-read-boundary-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00024 final local-only review

Review only the current diff for the P7.4 disabled Package02 non-empty
ungrouped commitment count read boundary. Check that false gates do not create
typed resources; true path uses only fixed profile/workload plus request
cancellation; malformed data fails closed; typed errors do not fall back to
legacy aggregate; other legacy page capabilities are not claimed migrated;
and no CE, traffic, ToolUtility removal or P8 action is included. Return only
Critical/Warning/Info findings with file references.


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