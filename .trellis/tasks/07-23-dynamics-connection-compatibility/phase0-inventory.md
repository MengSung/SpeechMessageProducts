# Phase 0: Legacy Dynamics Inventory Baseline

## Status

Started 2026-07-24. This is a read-only baseline for the no-SDK migration. It
does not alter product behavior, packages, credentials, or connection settings.

## Reproducible scan

~~~powershell
rg -l --glob "*.csproj" --glob "*.vbproj" --glob "*.fsproj" --glob "packages.config" --glob "!docs/**" --glob "!.trellis/**" --glob "!.ccg/**" "Microsoft\.Xrm|Microsoft\.CrmSdk|Microsoft\.Crm\.Sdk|Microsoft\.PowerPlatform\.Dataverse|Dynamics 365 SDK DLL" .

rg -l --glob "*.cs" --glob "*.vb" --glob "*.fs" --glob "!docs/**" --glob "!.trellis/**" --glob "!.ccg/**" "IOrganizationService|OrganizationServiceProxy|DiscoveryServiceProxy|OrganizationRequest|ICrmConnectionPool|ToolUtilityFactory|ToolUtilityClass|OrganizationData\.svc|XRMServices/2011" .
~~~

## Initial findings

| Area | Result | Phase 0 implication |
| --- | --- | --- |
| Project/package SDK references | 3 files | Final removal needs a project/package migration, not only source edits. |
| Solution/project graph SDK edges | 2 edges | The root solution includes PowerPlatform.Dataverse.Client, and ToolUtility references it directly. Both must be removed after consumers migrate. |
| Source files matching migration markers | 165 files | Each actual call site must enter the machine-readable Organization-call coverage matrix before a migrated source root can pass CI. |
| ChurchReport matching source files | 98 files | First consumer selection must be bounded; do not bulk-rewrite the product. |
| ToolUtility matching source files | 56 files | The legacy pool/factory and SDK-shaped interfaces require controlled-operation replacements. |
| PowerPlatform.Dataverse.Client matching source files | 3 files | The local GitHub-derived, WS-Trust-based client is explicitly in the final removal scope. |

### Confirmed project/package references

- ToolUtility.Tests/ToolUtility.Tests.csproj references
  Microsoft.CrmSdk.CoreAssemblies 9.0.2.56.
- PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj declares
  Microsoft.PowerPlatform.Dataverse.Client 1.1.32 and describes an
  IOrganizationService/WS-Trust implementation.
- SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj
  references Microsoft.PowerPlatform.Dataverse.Client 1.2.10 and a direct
  Microsoft.Crm.Sdk.Proxy.dll HintPath under the user-named Dynamics SDK DLL
  directory.
- SpeechMessageProducts.sln includes PowerPlatform.Dataverse.Client as a
  buildable project.
- ToolUtility/ToolUtility.csproj has a ProjectReference to
  ../PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj.

### Final removal rule

PowerPlatform.Dataverse.Client is temporary legacy only. It must not be wrapped,
renamed, or retained as the new connector. Final acceptance requires removing
the solution entry, removing ToolUtility's ProjectReference, deleting or moving
the project out of buildable source, and proving no production or test project
references Microsoft.Xrm, Microsoft.CrmSdk, Microsoft.Crm.Sdk,
Microsoft.PowerPlatform.Dataverse, IOrganizationService,
OrganizationServiceProxy, or DiscoveryServiceProxy.

## Interpretation

The scan is intentionally broad: a matching file is not automatically a
distinct CRM call site. The next Phase 0 artifact must normalize the 165
matching files into individual matrix rows with legacy operation shape, target
entity/action, proposed bounded capability, CE 8.2/9.1 evidence, parameter
encoding context, idempotency/audit class, migration status, owner, and removal
deadline.

## Next Phase 0 action

Call-site inventory is sufficiently mature at 70 normalized rows for first
package selection. Use
`phase0-migration-package-selection.md` as the readiness proposal:
Package 0 runtime foundation, then Package 1 fee reads for ChurchReport.
Do not bulk-normalize remaining branch variants unless they introduce a new
entity/operation shape. Phase 1 scaffolding starts only after package selection
is accepted.