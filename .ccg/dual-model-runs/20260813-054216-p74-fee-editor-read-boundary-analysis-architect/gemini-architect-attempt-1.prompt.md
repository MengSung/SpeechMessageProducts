ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p74-fee-editor-read-boundary-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG architect task: P7.4 fee editor read boundary

Repository: D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree
Active child: .trellis/tasks/08-13-p74-fee-editor-read-boundary

Analyse only; do not edit files, make CE calls, mutate data, enable flags, switch traffic, start P7.5/P8, push or create a PR.

The candidate is ORG-CALL-00066 `fees.editor.load.by.disciplelesson`. Typed Package01 API exists and has CE 9.1 Embedded read evidence. Current consumer:
FeeManagementController -> session-cached FeeList -> FeeDownUpLoader.ProcessDiscipleLesson -> CRM Entity + EntityCollection + repeated RetrieveEntity -> mutable Fee rows used by UpdateFeeData/SaveBatch/create/owner-assignment-adjacent flows.

We need decide whether a smallest P7.4 local-only, disabled-by-default, request-local DTO-only fee-editor read boundary can be carved safely. It must not rehydrate DTO into CRM Entity or mutable Fee, must not request-time fallback/dual-read/retry, must retain server-owned profile/workload and cancellation, and must not modify update/create/assign families.

Return:
1. exact safe candidate shape, if any (endpoint/projection/authorization/flag false behavior);
2. exact known DTO vs legacy view model parity gaps that prevent reusing the current editable grid;
3. required TDD tests for A/B isolation, cancellation, false-gate short circuit, no SDK Entity, no legacy fallback and read-only model;
4. no-go conditions and P7.5/P8 implications.
Classify Critical/Warning/Info. Do not propose unsafe shortcuts.

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