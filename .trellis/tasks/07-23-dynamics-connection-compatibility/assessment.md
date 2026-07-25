# Dynamics 365 8.2 and 9.1 connection compatibility assessment

## Conclusion

The checked-in `PowerPlatform.Dataverse.Client` project is an SDK/SOAP/WS-Trust
style connector, not a pure Dataverse Web API REST client. This assessment keeps
that fact only as a migration inventory record; it is **not** an approval to
reuse the project, its SDK assemblies, WCF/SOAP transport, or its credentials in
the new solution.

The selected end state is direct authenticated HTTP/OData v4 Web API with
explicit `v8.2` or `v9.1` profiles and real-server validation. Microsoft
documents the CE Web API as OData v4 with no required language-specific
assembly. The same no-SDK core supports a default Gateway host and a strict
product-selected Embedded host; Embedded is a process-location choice, not a
per-user CRM session/pool or an exemption from global organization admission.
No target is marked production-supported until its selected authentication mode,
service document, metadata, organization identity, capability matrix, and smoke
harness all pass.

## Source evidence

### Official compatibility evidence

- Microsoft, [Use the Dynamics 365 Customer Engagement Web API](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/use-microsoft-dynamics-365-web-api?view=op-9-1): CE Web API is OData v4 and Microsoft provides no language-specific assembly requirement.
- Microsoft, [Dynamics 365 Customer Engagement Web API versions](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/webapi/web-api-versions?view=op-9-1): v8.2 and v9.x need separate compatibility treatment; v9.x can have version-specific differences.
- Microsoft, [Authenticate to Dynamics 365 Customer Engagement with the Web API](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/webapi/authenticate-web-api?view=op-9-1): on-prem uses network credentials; IFD uses OAuth.
- Microsoft, [Use connection strings in XRM tooling](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/xrm-tooling/use-connection-strings-xrm-tooling-connect?view=op-9-1): CE on-prem OAuth requires AD FS and app registration, while certificate/client-secret authentication is documented for Dataverse rather than CE on-prem.

### Local inventory evidence

- `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj` targets `net10.0`, references `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32, and includes WCF packages such as `System.ServiceModel.Federation`, `System.ServiceModel.Http`, and `System.ServiceModel.Primitives`.
- The same project description says it offers an alternative `IOrganizationService` implementation using WS-Trust.
- `PowerPlatform.Dataverse.Client/OnPremiseClient.cs` implements `IOrganizationService`, reads WSDL using `?wsdl&sdkversion=`, and chooses either Active Directory or Federation.
- `ToolUtility/ConnectionOperations/CrmConnectionService.cs` creates this client in `CreateOnPremiseClient`.
- `ToolUtility/ConnectionOperations/CrmConnectionPool.cs` currently pools a single `serverUrl/username/password` set and disposes only services that implement `IDisposable`.
- `SpeechMessageProducts.ChurchReport/Startup.cs` currently registers one singleton `ICrmConnectionPool`, so simultaneous 8.2 and 9.1 usage needs named-profile pooling before production use.

## Compatibility matrix

| Target | Current custom OnPremiseClient | Direct Web API |
|---|---|---|
| Dynamics 365 CE 8.2 on-prem AD | Historical legacy path only; final solution forbids it. | Direct Web API candidate; requires Windows/IWA target-like smoke test and v8.2 capability checks. |
| Dynamics 365 CE 8.2 IFD/claims | Historical legacy path only; final solution forbids it. | Requires a proven non-password AD FS/OAuth workload flow; otherwise blocked. |
| Dynamics 365 CE 9.1 on-prem AD | Historical legacy path only; final solution forbids it. | Direct Web API candidate; requires Windows/IWA target-like smoke test and v9.1 capability checks. |
| Dynamics 365 CE 9.1 IFD/claims | Historical legacy path only; final solution forbids it. | Requires a proven non-password AD FS/OAuth workload flow; otherwise blocked. |
| Dataverse online / cloud | Not applicable to the final no-SDK connector. | Separate future OAuth/MSAL profile mode; not an implicit CE-on-prem compatibility claim. |

## Recommendation

Use JSON named profiles for switching 8.2 vs 9.1, but profile selection is
server-owned and selects a full **Web API** connection strategy, not just a
version string. Each product may use its own strict JSON file to choose the
trusted `Gateway` default or an `Embedded` host adapter. That file contains only
mode/binding shape; it never contains a CRM endpoint, credential, raw token,
user/LINE identity, or a per-user pool key. Both modes use the same controlled
capability operations and share the global organization-admission coordinator.

The recommended migration order is:

1. Create the no-SDK direct Web API connector and fake-server isolation tests.
2. Create the controlled Gateway, Embedded host adapter, profile-runtime pool,
   secret-reference resolution, strict product-mode JSON schema, and global
   organization-admission coordinator.
3. Complete CE 8.2 and CE 9.1 non-production smoke/performance/isolation gates.
4. Migrate one bounded, read-heavy product capability behind a feature flag;
   prove Gateway production mode and Visual Studio Embedded fake-server/local
   development mode without production secrets.
5. Migrate remaining capabilities individually; add a Web API operation only
   after its metadata/version parity decision is documented.
6. Remove the legacy SDK/SOAP path and add repository-wide no-SDK CI gates.

There is no SOAP/WCF fallback in the new Gateway. During strangler migration a
legacy product path may remain only as a temporary, documented product feature
flag outside the new solution; it is never selected by version detection and is
removed before final SDK-removal acceptance.

## Required smoke tests

- For each profile, call `WhoAmI` or Web API `WhoAmI` and assert the expected OrganizationId/UserId.
- Retrieve the Web API service document and `$metadata` to confirm the configured API route/capability and service availability. Record Discovery-service release data separately if an exact CE release assertion is required; the v8.x route/CSDL alone does not prove the release.
- Run representative contact/list queries used by ChurchReport.
- Run representative create/update/delete in a safe test entity or sandbox organization.
- Run repeated Web API acquire/release/reload calls and confirm no handler,
  socket, stream, timer, queue, token, or runtime-generation leak.
- Verify 8.2 and 9.1 profiles cannot share pool entries, tokens, cache entries, or credentials.
- Verify Gateway and Embedded hosts targeting the same organization share only
  the non-secret admission budget, never HTTP handlers, tokens, credentials,
  user/LINE/session data, or pooled connection objects.
