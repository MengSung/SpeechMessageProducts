ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: dynamics-82-91-sdk-bridge-assessment

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics 365 CE 8.2 / 9.1 SDK Bridge Architecture Assessment

## Role

Act as an enterprise integration architect. This is a read-only architecture assessment; do not modify source code.

## User decision context

- Proceed with a Local Gateway first.
- Keep Embedded deferred until Local Gateway validation completes.
- Decide whether the checked-in project `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj` must be retained or can eventually be removed.
- Product applications are ASP.NET Core / .NET 10.
- Target Dynamics servers include on-premises CE 8.2 IFD and CE 9.1.

## Repository facts already verified

1. The checked-in project is not Microsoft source. It is the Data8 open-source client:
   - targets `net10.0`
   - authors `MarkMpn, Data8 Ltd`
   - repository `https://github.com/Data8/DataverseClient`
   - provides `OnPremiseClient`, an `IOrganizationService` implementation using WS-Trust/SOAP
   - references the official `Microsoft.PowerPlatform.Dataverse.Client` package plus WCF/WS-Trust packages
   - its README says it is not officially supported by Data8 or Microsoft (best effort only)
2. Current dependency chain:
   - ChurchReport (.NET 10) -> ToolUtility (.NET 10) -> local Data8 project (.NET 10) -> `OnPremiseClient`
   - `ToolUtility/ConnectionOperations/CrmConnectionService.cs` constructs `new OnPremiseClient(...)`
   - deleting this project now would break the project reference/build and the existing legacy CRM connection path
3. Actual CE 8.2 IFD evidence:
   - SOAP / WS-Trust works
   - Web API + Windows NTLM returns an IFD redirect
   - OAuth password grant is rejected by ADFS
   - authorization-code OAuth cannot work yet because the OAuth client/redirect URI is not registered
   - no refresh token path is available yet
   - therefore Web API is blocked by infrastructure/auth configuration, not necessarily by the CE 8.2 Web API itself
4. Microsoft documentation says:
   - CE Web API is OData v4 and can be called directly with `HttpClient`
   - no language-specific assembly is required for Web API
   - on-premises Active Directory uses network credentials; IFD requires OAuth
   - CE 8.2 endpoint version is `/api/data/v8.2/`
5. Official package facts to consider:
   - modern `Microsoft.PowerPlatform.Dataverse.Client` / `ServiceClient` provides modern .NET assets, but on modern .NET the supported auth route for IFD is OAuth/certificate/client-secret rather than legacy WS-Trust username/password
   - official `Microsoft.CrmSdk.XrmTooling.CoreAssembly` / `CrmServiceClient` supports on-premises but runs on .NET Framework, not as a native .NET 10 component
6. The Local Gateway gives a process boundary: .NET 10 products can call an HTTP Gateway while an internal Windows/.NET Framework 4.8 worker can use an official legacy SDK if needed.

## Questions to answer

1. Does Dynamics CE 8.2 inherently require the checked-in Data8 project to integrate with ASP.NET Core / .NET 10, or is it only one compatibility bridge for the current WS-Trust/IFD authentication conditions?
2. What is the safest architecture for supporting both CE 8.2 and CE 9.1 from a Local Gateway?
3. For an official-SDK compatibility design, should 8.2 and 9.1 use one .NET Framework worker or separately version-pinned workers/processes? Explain binary/version/authentication risks and what must be tested before consolidation.
4. Can the Data8 project ultimately be removed? Provide precise prerequisites and migration/removal gates.
5. Compare these options:
   - direct Web API adapters for 8.2 and 9.1
   - official `ServiceClient` where OAuth is available
   - official .NET Framework `CrmServiceClient` compatibility worker
   - current Data8 .NET 10 WS-Trust bridge as temporary fallback
6. Give a recommended staged decision that preserves the currently working CE 8.2 path while moving toward official Microsoft components.

## Required output

- Traditional Chinese architecture recommendation
- Direct answers to questions 1-3 from the user
- Compatibility/risk table
- Recommended component/process diagram in text or Mermaid
- Explicit immediate decision: retain or remove the checked-in Data8 project now
- Explicit final-state decision and measurable removal criteria
- Flag any conclusion that requires real-server validation rather than documentation alone


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