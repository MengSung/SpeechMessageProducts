# Findings

## Current Status

As of 2026-07-07 20:35 Asia/Taipei, full Gemini + Claude dual-model analysis/review is still not available because Gemini is externally blocked by provider balance/quota.

The local CCG runner now behaves correctly:

- Gemini is classified as `provider-quota-or-billing-blocked` after the provider returns HTTP 403 with `{"error":"余额不足"}`.
- Claude is no longer using the user-level Fable setting by accident. It is launched through a process-only `claude.cmd` shim that injects `--model %CLAUDE_MODEL%` when the wrapper does not pass a Claude model.
- Claude currently passes health checks and can complete analyzer/reviewer prompts.
- Analyzer and reviewer runs can proceed only as accepted degraded fallback when `-AllowSingleModelWhenQuotaBlocked` is enabled: `completedBackends=["claude"]`, `failedBackends=["gemini"]`.
- This degraded fallback is usable for continued work, but it is not a completed Gemini + Claude dual-model result.

## Root Cause

- `codeagent-wrapper` supports Gemini model selection but has no Claude model flag. Setting `CLAUDE_MODEL=sonnet` alone did not force the wrapper-launched Claude process to use Sonnet.
- The runner now creates a process-unique shim directory under the system temp directory, records it in `CCG_CLAUDE_MODEL_SHIM_DIR`, and prepends that directory only to the current process PATH.
- The shim records `CLAUDE_MODEL_SHIM` and `CLAUDE_REAL_COMMAND` in health and run summaries for diagnosis.
- The shim is written via a temporary file and then `Move-Item -LiteralPath ... -Force`, avoiding a shared fixed shim path and non-atomic overwrite race during concurrent runs.
- Gemini remains externally blocked by the configured proxy/provider balance. This is not locally repairable in the runner.
- Gemini configuration inspection confirms `C:\Users\Administrator\.gemini\.env` selects `GOOGLE_GEMINI_BASE_URL=https://right.codes/gemini`, `GEMINI_MODEL=gemini-3-pro-preview`, and API-key auth. No alternate local Google OAuth credential or official Google API key configuration was found.

## Verification

- Regression guard:
  - `.ccg/tasks/fix-dual-model-operation/verify-claude-model-shim.ps1`
  - Expected result: `CLAUDE_MODEL_SHIM_GUARD_OK`
- PowerShell parse checks target:
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`
  - `docs/scripts/Test-CcgDualModelHealth.ps1`
- Health smoke:
  - `.ccg/dual-model-runs/ccg-health-20260707-203503.json`
  - Gemini: `Ok=false`, `FailureReason=provider-quota-or-billing-blocked`, diagnostic includes `{"error":"余额不足"}` with HTTP 403.
  - Claude: `Ok=true`, `FailureReason=ok`.
  - Environment shows a process-unique Claude shim path such as `ccg-claude-model-shim-33112-53d08dbc567143d980499a5c2473ff46\claude.cmd`.
- Fresh health smoke:
  - `.ccg/dual-model-runs/ccg-health-20260707-204130.json`
  - Gemini: `Ok=false`, `QuotaBlocked=true`, `FailureReason=provider-quota-or-billing-blocked`, diagnostic includes `{"error":"余额不足"}` with HTTP 403.
  - Claude: `Ok=true`, `FailureReason=ok`.
  - Summary note says at least one backend is blocked by provider quota/session state and cannot be repaired locally.
- Analyzer smoke:
  - `.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/summary.json`
  - `degradedFallback=true`, `fallbackAccepted=true`, `completedBackends=["claude"]`, `failedBackends=["gemini"]`.
- Reviewer smoke:
  - `.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/summary.json`
  - `degradedFallback=true`, `fallbackAccepted=true`, `completedBackends=["claude"]`, `failedBackends=["gemini"]`.
- Gemini configuration inspection:
  - User settings select `security.auth.selectedType=gemini-api-key`.
  - The only active Gemini base URL found is `https://right.codes/gemini`.
  - No relevant `GEMINI`, `GOOGLE`, or `GENAI` environment credential was present beyond `GEMINI_CLI_TRUST_WORKSPACE=true`.
  - Latest Gemini client error report still shows the provider response body `{"error":"余额不足"}`.
- Required external code review after Claude reset:
  - `.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/summary.json`
  - Gemini was blocked.
  - Claude produced usable review output but the wrapper-level run timed out.
  - Claude found a Critical issue: the original shim used a shared fixed temp directory and non-atomic write.
  - The Critical issue has been fixed with process-unique shim directories and atomic temp-file promotion.

## Remaining External Action

To restore full dual-model analysis/review:

1. Recharge or replace the Gemini provider credentials/base URL so Gemini no longer returns `余额不足`.
2. Rerun health, analyzer smoke, and reviewer smoke through `Start-CcgDualModelRun.ps1`.
3. Treat results as full dual-model only when both Gemini and Claude complete successfully in the same run.
