ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p7-2-slice-c-fresh-fixture-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice C fresh-fixture final review

Review the complete current `git diff` in this worktree. The diff currently
contains the Slice C fresh-fixture ledger, C# live gate/evidence changes,
PowerShell parent control plane and contract tests, and the task continuation.

This is high-risk CE test-fixture control-plane code. Review for correctness,
security, cross-user/profile isolation, no-retry behavior, resource ownership,
temporary-file/reparse defenses, Data8 lease lifecycle, evidence sanitization,
CRLF/UTF-8 boundaries, and test adequacy.

Known current live result: one newly authorized provision run returned the
sanitized no-go category `baseline-owner-unavailable`; all five operation
families remained `not-run`. This is expected when the descriptor-bound
task-marked leader belongs to the Data8 `WhoAmI` user. It is not permission to
auto-select an owner or weaken self-assignment prevention.

Do not suggest changes that broaden CRM writes, use CE 8.2, Official Worker,
automatic retries, profile/connector switches, feature-flag changes, or raw
identifier/credential logging. Do not modify files.

Output a concise Traditional Chinese Critical / Warning / Info report with
file/line references for actionable findings. Confirm separately whether the
current no-go path preserves the expected no-mutation guarantee for the
baseline-owner condition.


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
