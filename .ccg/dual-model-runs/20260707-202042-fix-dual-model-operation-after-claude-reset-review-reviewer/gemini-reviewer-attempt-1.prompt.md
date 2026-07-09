ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: fix-dual-model-operation-after-claude-reset-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
Review the current git diff for the CCG dual-model runner repair. Focus on correctness, PowerShell 5.1 compatibility, process-only Claude model shim behavior, provider quota/session classification, path pollution risk, encoding/line ending requirements, and whether the analyzer/reviewer fallback semantics are honest. Run git diff locally. Output Critical / Warning / Info findings.

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