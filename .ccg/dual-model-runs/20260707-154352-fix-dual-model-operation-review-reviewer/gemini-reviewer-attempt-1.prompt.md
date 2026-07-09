ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: fix-dual-model-operation-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
# Review request: fix CCG dual-model operation

Please review the following changes for correctness, maintainability, and failure-mode handling. Focus on:

- Claude default model handling via CLAUDE_MODEL=sonnet.
- Gemini quota/billing classification and diagnostics.
- Health/smoke summary fields and degraded fallback behavior.
- Any risk that provider failures could be misreported as full dual-model success.

Return findings as Critical / Warning / Info.

```diff
System.Object[]
```

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