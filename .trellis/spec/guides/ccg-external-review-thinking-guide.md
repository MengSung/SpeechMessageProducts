# CCG External Review Thinking Guide

> Use this before running or repairing Gemini/Claude CCG external review. Full runbook: `docs/ccg-dual-model-health-permanent-fix.md`.

## Standard Entry

For CCG analysis or review, use the self-healing runner first:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1" `
  -TaskFile ".\.ccg\dual-model-runs\<task>.md" `
  -Role reviewer `
  -RepositoryPath "<worktree-root>" `
  -OutputDirectory ".\.ccg\dual-model-runs"
```

Do not start by debugging Gemini or Claude manually. The runner owns PATH setup,
UTF-8 environment setup, backend smoke checks, retries, and summary output.

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
- exit code `2`: local toolchain still needs repair; inspect the run folder health/stdout/stderr files.
- `-AllowSingleModelWhenQuotaBlocked`: allowed only when the task owner accepts a fallback; never call it a completed dual-model review.
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
