# Dynamics 365 8.2 and 9.1 connection compatibility analysis

## Decision to support

Assess whether the checked-in `PowerPlatform.Dataverse.Client` project can safely connect to both Dynamics 365 Customer Engagement 8.2 and 9.1 on-premises instances, and whether a no-SDK Web API connector is the better strategic replacement.

## Repository evidence to inspect

- `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`
- `PowerPlatform.Dataverse.Client/OnPremiseClient.cs`
- `ToolUtility/ConnectionOperations/CrmConnectionService.cs`
- `ToolUtility/ConnectionOperations/CrmConnectionPool.cs`
- `ToolUtility/Factories/CrmClientFactory.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`

## Questions

1. Does this project use an SDK/SOAP client or direct Web API calls? What authentication modes does it support?
2. Is a 9.x `Microsoft.Xrm.Sdk`-based `OnPremiseClient` likely compatible with both 8.2 and 9.1? Identify facts that must be validated against a real server.
3. What breaks or remains risky when a single process needs two named CRM profiles (8.2 and 9.1)? Focus on tenant/profile isolation, service lifetime, disposal, pooling, and credential handling.
4. Compare a Web API-first (`HttpClient` + OData v4) connector with the current SDK/SOAP path. Include known Dynamics 365 8.2 Web API feature gaps and migration constraints.
5. Recommend a configuration-driven target architecture and a phased migration order. Do not make code changes.

## Constraints

- The user wants to avoid SDK framework and version coupling where practical.
- Do not recommend the deprecated CRM 2011 `OrganizationData.svc` / OData v2 endpoint.
- Do not assume that a 9.1 server is online; distinguish AD on-premises, IFD on-premises, and online OAuth.
- Treat cross-profile data, token, connection, or cache leakage as a release blocker.

## Required output

Return an evidence-oriented report with sections: `Conclusion`, `Source evidence`, `Compatibility matrix`, `Risks`, `Recommended architecture`, and `Validation tests`. Classify findings as Critical, Warning, or Info.
