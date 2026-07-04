# CCG Dual Model Self-Healing Review

## Verification

- PowerShell syntax parse passed for `docs/scripts/Test-CcgDualModelHealth.ps1`.
- PowerShell syntax parse passed for `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`.
- Health check finds `codeagent-wrapper.exe`, `gemini.cmd`, `claude.cmd`, and `python.exe`.
- Gemini backend smoke succeeded through `codeagent-wrapper.exe --lite --backend gemini`.
- Claude backend is logged in, but currently blocked by provider session limit: `You've hit your session limit · resets 1:30pm (Asia/Taipei)`.
- Runner now correctly classifies Claude wrapper exit 1 as `quotaBlocked=true` by running a direct Claude probe.

## Result

The local self-healing workflow is working for repairable issues and now correctly separates external provider quota/session-limit failures from local toolchain failures. It does not fake a complete dual-model review when Claude is quota-blocked.