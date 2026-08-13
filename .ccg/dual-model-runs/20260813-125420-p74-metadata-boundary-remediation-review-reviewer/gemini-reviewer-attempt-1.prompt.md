ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p74-metadata-boundary-remediation-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 metadata-boundary remediation review

Review only the current task-scoped diff for the following files:

- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- `.trellis/tasks/08-13-08-13-p74-metadata-boundary-review-remediation/`

Required contract:

1. With the Package02 base gate false, do not bind options or compose a host/provider/pool/handler/credential graph.
2. With the gate true, validate deployment-owned `ProfileAlias` before returning an injected facade or resolving a host. No request, session, caller or facade may select it.
3. No feature gate enablement, CE request/mutation, fixture, traffic switch, ToolUtility removal, P7.5 or P8 work is permitted.
4. Preserve request/profile isolation and deterministic resource ownership; no retry/fallback after a typed failure.
5. Verify test changes demonstrate the blank-profile failure and valid-profile injected-client case.

Classify only concrete findings as Critical, Warning, or Info. Do not propose scope expansion.


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
