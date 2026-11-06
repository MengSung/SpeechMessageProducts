ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: p105-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
Read-only analysis of P1-05 Localization implementation. Do not modify files or read credentials/config secrets. Read formal plan 02 P1-05 and appendix 90 C.1/E.3, SpeechMessage.Localization and its tests. Evaluate five-layer original-versus-expanded fallback, immutable authorized scope, named parameter parsing/format cultures, immutable override snapshots and stable IDs, enabled/released language resolver and production pseudo rejection, NFKC keyword conflicts. Identify concrete risks and focused validation. Existing P1-04 is accepted. Main session is inline and will implement/check; no child dispatch. Provide concise Critical/Warning/Info with file evidence. Do not access VM or remote services.


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