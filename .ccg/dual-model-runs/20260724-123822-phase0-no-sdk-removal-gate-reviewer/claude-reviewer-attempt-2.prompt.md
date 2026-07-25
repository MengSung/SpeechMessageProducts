ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: phase0-no-sdk-removal-gate

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Review request: Phase 0 no-SDK removal gate

Role: reviewer

Please review the Phase 0 Dynamics 365 no-SDK removal gate updates.

User requirement:

- Final solution must not reference any Microsoft CRM/Dataverse SDK DLL/package/type.
- PowerPlatform.Dataverse.Client is a temporary legacy dependency only.
- PowerPlatform.Dataverse.Client must ultimately be removed from SpeechMessageProducts.sln.
- All ProjectReference/package/DLL references to PowerPlatform.Dataverse.Client and Microsoft CRM SDK assemblies must be removed after consumers migrate.
- Phase 0 must not break the current build by deleting the legacy project prematurely; it should record inventory/gates only.

Files to inspect:

- .trellis/tasks/07-23-dynamics-connection-compatibility/prd.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/design.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/implement.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-inventory.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-runtime-capacity-adr.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-verification.md
- docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md
- .ccg/tasks/dynamics-connection-compatibility/task.json

Known current SDK graph that must be represented as final-removal findings:

- SpeechMessageProducts.sln includes PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj.
- ToolUtility/ToolUtility.csproj ProjectReference includes ../PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj.
- ToolUtility.Tests/ToolUtility.Tests.csproj references Microsoft.CrmSdk.CoreAssemblies 9.0.2.56.
- PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj references Microsoft.PowerPlatform.Dataverse.Client 1.1.32 and Microsoft.Xrm.Sdk source types.
- SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj references Microsoft.PowerPlatform.Dataverse.Client 1.2.10.
- SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj references Microsoft.Crm.Sdk.Proxy.dll through a Dynamics 365 SDK DLL HintPath.

Verification already run locally:

- JSON validity: schema and matrix parse with ConvertFrom-Json.
- Mandatory new SpeechMessage.Dynamics.sln stale wording scan found no rejected wording.
- Final no-SDK removal wording scan found expected references.
- git diff --check on focused artifacts passed.

Please check:

1. Does the Phase 0 inventory/gate fully represent the current SDK reference graph?
2. Is the final no-SDK end state unambiguous that PowerPlatform.Dataverse.Client must be removed/deleted or moved out of buildable source, not wrapped or retained?
3. Does Phase 0 avoid prematurely deleting references before the replacement no-SDK path exists?
4. Are there any contradictions with the existing-solution topology, i.e., add new Dynamics projects to SpeechMessageProducts.sln rather than require a new SpeechMessage.Dynamics.sln?
5. Are there missing Critical/Warning findings around session leakage, memory leakage, connection pooling, or SDK-removal enforcement?

Output: Critical / Warning / Info findings with file references and concise remediation.



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