ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p75-prerequisite-evidence-planning

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.5 offline prerequisite evidence planning review

Review only the planned repository-local P7.5 prerequisite evidence task.

Known baseline: The immutable 70-row authoritative gap matrix says 3 consumer rows are migrated-disabled, 67 are not migrated, every row remains temporary-legacy; P7.5 blockers are 49 consumer-not-migrated, 13 mixed, 5 special-resource-pending, 3 legacy-sdk-dependency. Historical P7.2 Slice C is write-not-committed/no-go-closed and cannot be retried.

Proposed task: create an offline, deterministic scanner/validator under the active Trellis task. It will inspect only ChurchReport production .cs, .csproj and explicitly allowlisted necessary settings; exclude tests/docs/bin/obj. It must strip C# comments/string literals, emit only bounded de-identified categories/counts, validate the immutable matrix but never rewrite it, and fail closed. It will create an ordered capability-family blocker report. It must not perform CE/network/browser/credential/feature-gate/traffic/P7.5 removal/P8 action.

Review for false-completion, scanner evasion/false positives, security isolation, encoding/line ending, resource lifecycle, test strategy, and scope boundaries. Do not propose operational CE/deployment actions. State Critical/Warning/Info and concise remedies.


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