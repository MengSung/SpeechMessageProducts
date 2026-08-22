# CCG reviewer Task: churchreport-trace-remediation-f4-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# F4 background trace boundary review

Review the current uncommitted changes for F4 only. Inspect `git diff` and the relevant source/tests. Do not modify files.

Requirements:

- `DataverseTrace.BeginBackgroundOperation(string operationName)` creates a child trace `{parentTraceId}#bg{seq}`, a new statistics object, keeps the parent's pseudonymous user, clears any inherited lease, emits `bg.begin`/`bg.end`, and restores only the background flow's prior context on Dispose.
- Parent `request.end` metrics must not include background CRM work; nested and parallel backgrounds must be isolated.
- `bg.end` contains all request aggregate fields plus `parentTraceId` and `op`; no user-controlled or secret data enters the operation name.
- ToolUtility stays host-neutral. No pool/gateway lifecycle changes.
- SaveIntegrate opens the scope before its background DI scope.
- Tests must genuinely protect the contracts and C# documentation must satisfy the project's Traditional Chinese lifecycle/isolation requirements.

Report only verified Critical/Warning/Info findings with file/line evidence.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.