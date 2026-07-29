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
