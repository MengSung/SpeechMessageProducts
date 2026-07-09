ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: dual-model-smoke-test-20260707-warning-fix

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
# CCG dual-model smoke test

Please respond with a short health-check result:

- Confirm the backend name you are running under, if available.
- State whether you can read this repository path.
- Do not inspect or modify files.



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