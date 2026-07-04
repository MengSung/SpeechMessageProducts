# CCG Dual Model Auto Recovery Entrypoint

## Requirement
Future CCG analysis/review work must not stop when Gemini, Claude, or `codeagent-wrapper` fails before producing usable findings. The default entrypoint must preserve the prompt, run the self-healing runner, repair known local PATH/UTF-8/trust/toolchain issues, retry the same role, and continue when at least one authorized backend result is usable.

## Acceptance Criteria
- Provide a simple script entrypoint for analysis/review that creates the UTF-8 task prompt file and calls `Invoke-CcgDualModelWithSelfHealing.ps1`.
- Keep all run artifacts under `.ccg/dual-model-runs`.
- Keep provider quota/session failures clearly classified; never claim degraded fallback is full dual-model success.
- Document that future agents should call this entrypoint automatically before giving up on dual-model review.