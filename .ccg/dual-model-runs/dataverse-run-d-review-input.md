# Run D Dataverse legacy path removal review

Review the current uncommitted Run D changes for `.trellis/tasks/08-18-dataverse-gateway-architecture-v1`.

Required behavior:

- `ToolUtilityClass` must no longer create, own, or dispose raw CRM connections.
- `ToolUtilityFactory` remains a legacy singleton API, but may retain only `AmbientGatewayOrganizationService`; it must never retain an HTTP request scope, gateway instance, lease, raw client, identity, or request service provider.
- Startup configures the ambient proxy using a lazy current-request accessor and the root `IServiceScopeFactory`.
- A missing HTTP request must create and dispose a short-lived scope per operation.
- A timeout/fault boundary remains owned by the existing Gateway/pool; no legacy branch may expose `m_OrganizationService`.
- The `m_Crm2011OrganizationService` public compatibility field must continue to exist but now hold a gateway proxy.
- The two ActivityAttachment branches are compile-time false and must preserve only their former else calls.
- All `m_OrganizationService` code references must be gone after excluding comments; `CrmConnectionPool.cs` is intentionally deleted and the existing adapter remains.
- No modified code may introduce cross-request/user leakage, resource leakage, captive scoped dependency, accidental raw-client disposal, or scope reuse.

Run D tests use fake `IOrganizationService` only. The MemberInfo baseline is 22 failures / 305 passes and is not a regression.

Output only Critical / Warning / Info findings with exact file and line evidence. Verify every claim against the current worktree; do not request scope expansion.
