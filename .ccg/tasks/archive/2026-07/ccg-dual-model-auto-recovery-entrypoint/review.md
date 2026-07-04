# CCG Dual Model Auto Recovery Entrypoint Review

## Completed Changes

- Added `docs/scripts/Start-CcgDualModelRun.ps1` as the standard high-level entrypoint for future CCG analysis/review.
- Updated `AGENTS.md` so future agents must start from `Start-CcgDualModelRun.ps1` instead of direct Gemini, Claude, or `codeagent-wrapper` commands.
- Updated `.trellis/spec/guides/ccg-external-review-thinking-guide.md` to make the auto-recovery entrypoint the standard recovery path.
- Rewrote `docs/ccg-dual-model-health-permanent-fix.md` as a readable UTF-8 Traditional Chinese permanent runbook.
- Updated `/ccg:analyze` and `/ccg:review` command templates under `C:\Users\Administrator\.claude\commands\ccg\` to use `Start-CcgDualModelRun.ps1`.

## Verification

- PowerShell parser check passed for:
  - `docs/scripts/Start-CcgDualModelRun.ps1`
  - `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`
  - `docs/scripts/Test-CcgDualModelHealth.ps1`
- UTF-8/mojibake scan passed for the updated docs and scripts.
- Health check passed without backend smoke; wrapper, Gemini, Claude, and Python paths were found.
- Full smoke run through `Start-CcgDualModelRun.ps1` passed.
- Smoke run completed both Gemini and Claude successfully with `ok=true`, `quotaBlocked=false`, and `degradedFallback=false`.

## Result

Future CCG analysis/review has a stable automatic recovery path. If Gemini, Claude, or `codeagent-wrapper` fails before usable output, future agents should call `Start-CcgDualModelRun.ps1`; the script preserves the prompt, delegates to the self-healing runner, repairs local toolchain issues where possible, retries, and writes a structured summary under `.ccg/dual-model-runs`.