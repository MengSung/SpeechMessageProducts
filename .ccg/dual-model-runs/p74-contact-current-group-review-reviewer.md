# CCG reviewer Task: p74-contact-current-group-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00052 source-audit review

Review only the current task artifacts under
`.trellis/tasks/08-14-08-14-p74-contact-current-group-read-boundary/` and the
source they cite: `ContactService.GetContactCurrentGroup` plus
`AddContactToListAsync`.

Verify whether `source-only-local-design-no-go` is justified and whether the
record incorrectly claims any runtime, CE, feature gate, consumer, traffic,
P7.5 or P8 progress. Report Critical/Warning/Info findings only. Do not propose
partial read cutover, CE work, generic Entity bridge, retries, fallback, or
caller-owned authorization.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.