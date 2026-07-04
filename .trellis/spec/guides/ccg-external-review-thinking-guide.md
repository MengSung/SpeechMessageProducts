# CCG External Review Thinking Guide

> Use this before running or repairing Gemini/Claude CCG external review. Full runbook: `docs/ccg-dual-model-health-permanent-fix.md`.

## Standard Entry

For CCG analysis or review, use the project auto-recovery entrypoint first. This
is also the automatic recovery path when Gemini, Claude, or `codeagent-wrapper`
fails before producing usable findings.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Start-CcgDualModelRun.ps1" `
  -Role reviewer `
  -Title "<short-task-name>" `
  -PromptFile ".\.ccg\dual-model-runs\<task>-review-input.md" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs" `
  -AllowSingleModelWhenQuotaBlocked
```

Do not start by debugging Gemini or Claude manually. The entrypoint creates the
UTF-8 task prompt and delegates to `Invoke-CcgDualModelWithSelfHealing.ps1`,
which owns PATH setup, UTF-8 environment setup, backend smoke checks, retries,
and summary output.

## Mandatory Recovery Loop

When a dual-model analysis/review fails:

1. Preserve the original task prompt, or create one under `.ccg/dual-model-runs/` if the failed call
   was typed directly.
2. Run `Start-CcgDualModelRun.ps1` with the same role and repository path.
3. Read the generated `summary.json`.
4. If `ok=true`, continue the task from Gemini + Claude outputs.
5. If exit code is `2`, repair the local issue shown in the run folder and rerun the same runner.
6. If `quotaBlocked=true`, stop calling it a tool failure; this is provider/session state.
   In this project, the owner has approved `-AllowSingleModelWhenQuotaBlocked` when one backend
   succeeded with usable output, but the result must be reported as degraded fallback rather than
   completed dual-model success.

Do not abandon the development task just because the first Gemini/Claude call failed. The default
response is self-heal, rerun, then continue.

## Standing Fallback Policy

The project owner has approved continuing work when one external model is blocked by
provider quota/session limits, as long as the other model completed and produced usable
findings. This is a degraded review/analysis, not a completed dual-model result.

Use this distinction every time:

- Repairable local/toolchain failure: run the self-healing runner, fix the local issue, and retry.
- Provider quota/session failure with one successful backend: continue from the successful backend,
  record the blocked backend in the final report, and do not claim full dual-model success.
- Provider quota/session failure with no successful backend: do not treat the review/analysis as
  complete; rely on local tests and code inspection, then retry the external review later.
- Actual model finding marked Critical: never ignore it merely because the other backend failed.
  Verify the finding against the code and fix it or document the technical reason it is not valid.

## Quick Trigger

Read the full runbook when any of these appear:

- `gemini command not found in PATH`
- `claude command not found in PATH`
- `npm.ps1 cannot be loaded`
- Claude says it is not logged in or has no API key
- Gemini hangs, crashes, or reports a Windows libuv assertion
- Gemini hooks report `python not recognized`
- A new worktree causes Gemini trust / approval problems

## Required Health Check

If you only need a health check without running a review prompt:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs"
```

## Stable Reviewer Shape

Use:

```powershell
codeagent-wrapper.exe --lite --backend gemini
codeagent-wrapper.exe --lite --backend claude
```

Do not use Gemini with `--progress` on Windows unless the wrapper/Gemini crash path has been revalidated.

## Failure Classification

- `ok=true`: both backends completed.
- `quotaBlocked=true`: external provider quota/session limit; not locally repairable.
- `degradedFallback=true`: one backend completed, another backend was quota/session blocked,
  and `-AllowSingleModelWhenQuotaBlocked` was enabled. Continue the task, but report this as
  single-model fallback rather than full dual-model success.
- exit code `2`: local toolchain still needs repair; inspect the run folder health/stdout/stderr files.
- exit code `3`: provider quota/session blocked the run and no accepted single-model fallback is available.
  Do not repair local tooling for this state; retry later or continue only with local verification while
  clearly reporting that external review is incomplete.
- `-AllowSingleModelWhenQuotaBlocked`: allowed by the current project owner for quota/session fallback;
  never call it a completed dual-model review.
- Health backend smoke is skipped by default to avoid burning quota before the real analysis/review; use `-RunHealthBackendSmoke` only when explicitly diagnosing backend login/provider state.

## Mental Model

Treat CCG external review as a multi-layer integration:

1. Windows User PATH
2. npm global shims
3. Codex sandbox / escalated execution
4. `codeagent-wrapper`
5. Gemini / Claude auth and trust state
6. Python hooks under `.gemini/hooks`
7. CCG reviewer prompt templates

Do not assume one passing layer proves the whole chain works.
