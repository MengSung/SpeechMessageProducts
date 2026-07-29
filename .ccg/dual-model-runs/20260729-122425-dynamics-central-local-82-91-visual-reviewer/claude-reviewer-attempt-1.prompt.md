ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-central-local-82-91-visual

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Review: Central/Local Gateway with Dynamics CE 8.2 and 9.1 visualization

Perform a read-only architecture and visualization review. Do not modify files.

## Artifact

`C:/Users/Administrator/.codex/visualizations/2026/07/29/019fab98-842e-78a1-9b65-ee684c875612/dynamics-central-local-82-91.html`

Rendered preview:

`C:/Users/Administrator/.codex/visualizations/2026/07/29/019fab98-842e-78a1-9b65-ee684c875612/dynamics-central-local-82-91-preview.png`

## Intended decisions

1. Products use one shared ProductClient/REST contract.
2. Product configuration chooses `CentralGateway` or `LocalGateway` at deployment/startup and supplies a `ProfileAlias`; it does not supply CRM credentials or arbitrary endpoints.
3. Central Gateway is the production default and owns centrally shared profile runtimes/pools.
4. Local Gateway is a per-product out-of-process Windows service/console for Visual Studio development or isolated deployments. Its physical connection pool is process-local.
5. Central and Local pools are physically separate, but all hosts targeting the same physical Dynamics organization share an organization-level aggregate admission/concurrency budget.
6. Both gateway modes use the same adapter contract and profile routing model.
7. CE 9.1 preferred path is direct Web API v9.1 or Microsoft's official ServiceClient when supported authentication is available.
8. CE 8.2 does not inherently require Data8. Current 8.2 IFD conditions make the working Data8 WS-Trust bridge temporarily necessary.
9. CE 8.2 target replacements are either direct Web API after ADFS OAuth is proven, or an out-of-process .NET Framework 4.8 worker using Microsoft's official CrmServiceClient.
10. CE 8.2 and 9.1 legacy SDK workers should initially remain independently version-pinned/process-isolated. Consolidation is allowed only after real-server compatibility and lifecycle testing.
11. Data8 remains temporary and can be removed only after the replacement passes real-server tests and all project/source references are removed.
12. Embedded remains deferred and is intentionally not part of this visual's recommended execution modes.

## Review questions

- Is the architecture technically accurate and consistent with the stated decisions?
- Does any wording incorrectly imply that all versions/identities share one mutable connection/session?
- Does any wording overclaim official ServiceClient or Web API support for the current CE 8.2 IFD environment?
- Is the Central vs Local ownership boundary understandable?
- Are the Data8 retention/removal and official-worker migration boundaries clear?
- Are there isolation, credential, connection-pool, or resource-lifecycle risks missing from the diagram?
- Report Critical / Warning / Info findings. If there are no Critical or Warning findings, say so explicitly.


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