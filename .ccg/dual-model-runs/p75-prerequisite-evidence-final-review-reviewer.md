# CCG reviewer Task: p75-prerequisite-evidence-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.5 prerequisite evidence final review

Review the current uncommitted P7.5 task changes only. Verify that the Python scanner remains offline, bounded, sanitized and fail-closed; that C# directives and JSONC comments cannot create false results; that no report state claims ToolUtility removal, CE evidence, traffic cutover, or P8 readiness; and that the expected current `--enforce-p75` no-go is correctly treated as a valid gate result. Return Critical/Warning/Info findings with file references; do not request external systems or credentials.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.