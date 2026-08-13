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
