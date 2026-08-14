# CCG reviewer Task: p74-00057-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 ORG-CALL-00057 final review

Review only the P7.4 local-only App-named membership read data plane.

Scope:

- `SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs`
- `SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs`
- `SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs`
- `SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs`
- `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs`
- `SpeechMessage.Dynamics.ProductClient/ListCatalog/AppNamedMembership*.cs`
- `SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs`
- `SpeechMessage.Dynamics.Tests/AppNamedMembership*.cs`
- `SpeechMessage.Dynamics.Tests/Package01OperationRegistryTests.cs`
- current Phase-0 matrix row/schema only.

Contract:

- `ORG-CALL-00057` is a default-disabled, DTO-only, local read data plane.
- It accepts only server/deployment-owned profile/workload and an already-authorized non-empty contact GUID.
- Query is fixed to active + app-named `list` records related by `listmember.entityid`, projects only list ID/name, sorts deterministically, and fails closed for paging, malformed rows, duplicate IDs, or 32-row/32-KiB excess.
- No ChurchReport/ToolUtility consumer, feature gate, traffic switch, CE request, fixture, retry, fallback, cache, raw CRM Entity, or archived P7.2 fixture alteration is permitted.
- Review cross-user/profile isolation, resource ownership, response validation, matrix/schema synchronization, and test coverage.

Verification already run:

- focused AppNamedMembership + OperationRegistryAgreement: 22 passed;
- full Dynamics: 877 passed, 7 environment tests skipped;
- full solution: passed; ChurchReport 634 passed, 14 live-environment tests skipped;
- Release build: 0 warnings/errors.

Return only Critical / Warning / Info findings. Do not recommend consumer cutover or CE work.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.