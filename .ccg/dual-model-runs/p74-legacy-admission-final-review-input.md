# P7.4 Legacy admission boundary final review

Review the current uncommitted diff for task
`.trellis/tasks/08-13-p74-legacy-gateway-admission/` and directly related P7.4 parent
records. Classify only verified findings as Critical, Warning, or Info.

Required invariants:

- No feature flag, CE mutation, traffic cutover, P7.5 or P8 enablement.
- `Package01FeeReadsEnabled` must be false in checked-in appsettings and DedicatedGateway
  launch settings.
- The controller may meter only registered local legacy work; it must not claim durable
  cross-host admission, complete legacy coverage, or cancellation of synchronous CRM I/O.
- No request/session/profile/credential/CRM entity retention; deterministic bounded cleanup.
- PID evidence reader may retry only Windows sharing/lock violations 32/33 within a fixed
  deadline; unexpected filesystem errors must remain fail-closed.
- Review UTF-8/CRLF and task claims for evidence inflation.

Do not recommend enabling a flag or changing external deployment state. Output a concise
Critical/Warning/Info report and final verdict.
