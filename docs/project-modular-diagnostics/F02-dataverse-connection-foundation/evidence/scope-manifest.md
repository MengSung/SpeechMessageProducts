# F02 Scope Manifest

Status: COMPLETE
Module: F02 - Dataverse Connection Foundation
Mode: DIAGNOSIS_ONLY
Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
Branch: `1.0.0.1.EvenVersion`
HEAD: `26781fd452743710aa7d9276f3ec9be50b29bc24`
Map source: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## Ownership Rule

The authoritative map assigns every tracked path under
`PowerPlatform.Dataverse.Client/**` to F02. This includes project lifecycle,
the public `OnPremiseClient`, WSDL and WS-Trust metadata discovery,
Active Directory and federated authentication, SOAP/WCF transport, SDK
wrapping, and the bundled NSspi source/project.

ChurchReport query shape, entity rules, connection-pool policy, and mixed CRM
facades are not F02-owned. They are read-only consumers or handoffs to F03A,
F03Q, X01, or X02C.

## Exact Primary-Owner Files

The map-authoritative tracked inventory contains 62 files: 55 C# files, two
project files, and five support assets.

Top-level F02 files:

- `PowerPlatform.Dataverse.Client/ADAuthClient.cs`
- `PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs`
- `PowerPlatform.Dataverse.Client/OnPremiseClient.cs`
- `PowerPlatform.Dataverse.Client/Wsdl.cs`
- `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`
- `PowerPlatform.Dataverse.Client/README.md`
- `PowerPlatform.Dataverse.Client/Data8.png`

AD authentication helpers:

- `PowerPlatform.Dataverse.Client/ADAuthHelpers/Authenticator.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/BaseAuthRequest.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/BinaryExchange.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/CombinedHash.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/EncryptedKey.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/FaultReader.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/Lifetime.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/Namespaces.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/RequestSecurityToken.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/RequestSecurityTokenResponse.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/RequestSecurityTokenResponseCollection.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/SecurityContextToken.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/SecurityHeader.cs`
- `PowerPlatform.Dataverse.Client/ADAuthHelpers/SecurityTokenReference.cs`

Bundled NSspi lifecycle and source:

- `PowerPlatform.Dataverse.Client/NSspi/.gitignore`
- `PowerPlatform.Dataverse.Client/NSspi/app.config`
- `PowerPlatform.Dataverse.Client/NSspi/ByteWriter.cs`
- `PowerPlatform.Dataverse.Client/NSspi/EnumMgr.cs`
- `PowerPlatform.Dataverse.Client/NSspi/NativeMethods.cs`
- `PowerPlatform.Dataverse.Client/NSspi/nsspi key.snk`
- `PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj`
- `PowerPlatform.Dataverse.Client/NSspi/PackageNames.cs`
- `PowerPlatform.Dataverse.Client/NSspi/PackageSupport.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Properties/AssemblyInfo.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SecPkgInfo.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SecurityStatus.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SSPIException.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SspiHandle.cs`
- `PowerPlatform.Dataverse.Client/NSspi/TimeStamp.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/ClientContext.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/Context.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/ContextAttrib.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/ContextNativeMethods.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/ContextQueries.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/ContextQueryAttrib.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/ImpersonationHandle.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/SafeContextHandle.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/SafeTokenHandle.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Contexts/ServerContext.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/AuthData.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/ClientCurrentCredential.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/Credential.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/CredentialNativeMethods.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/CredentialQueryAttrib.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/CredentialUse.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/CurrentCredential.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/PasswordCredential.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/QueryNameSupport.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/SafeCredentialHandle.cs`
- `PowerPlatform.Dataverse.Client/NSspi/Credentials/ServerCurrentCredential.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SecureBuffer/SecureBuffer.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SecureBuffer/SecureBufferAdapter.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SecureBuffer/SecureBufferDataRep.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SecureBuffer/SecureBufferDesc.cs`
- `PowerPlatform.Dataverse.Client/NSspi/SecureBuffer/SecureBufferType.cs`

## Public Contract And Data Flow

Public input:

- organization service URL;
- optional username/password through `ClientCredentials`;
- `IOrganizationService` requests;
- mutable operation timeout and `CallerId`.

F02 flow:

1. `OnPremiseClient` validates the initial URL scheme and downloads the
   organization WSDL.
2. WSDL policy selects Active Directory or federation.
3. AD creates a WS-Trust security context over HTTP SOAP; federation discovers
   an STS and creates a WCF channel.
4. F02 wraps `IOrganizationService` calls and injects SDK/CallerId headers.
5. Consumers receive only the `IOrganizationService` surface.

Output:

- an authenticated organization-service transport;
- Dataverse SDK responses or transport/authentication exceptions.

## Read-Only Dependencies And Consumers

| Path | Owner | Diagnostic use |
|---|---|---|
| `ToolUtility/ToolUtility.csproj:53` | F03A | Direct project reference to F02 |
| `ToolUtility/ConnectionOperations/CrmConnectionService.cs:430-439` | F03A | Constructs `OnPremiseClient` and erases its concrete lifecycle behind `IOrganizationService` |
| `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:52-90` | F03A/X01 policy | Eager minimum-pool creation and singleton lifetime context |
| `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:293-315` | F03A | Repeated F02 client construction |
| `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:406-419` | F03A | Consumer disposal attempt |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:138-158` | F03Q/F03A | Legacy direct client lifetime consumer |
| `SpeechMessageProducts.ChurchReport/Startup.cs:297-349` | X01/X04A | Singleton pool, configured min 3/max 20, credential source |
| `ToolUtility/Extensions/CrmAsyncExtensions.cs:330-367` | F03A | Potential parallel consumer of one service instance |
| `SpeechMessageProducts.sln:12` | F01A | Main F02 project is enrolled |
| `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:62` | X01 | Host also references Microsoft's Dataverse package |

Contract consumers from the map are F03A, F03Q, and X02C. The host consumes F02
transitively through ToolUtility and the connection pool.

## Tests And Gate State

- No F02-owned test project, `[Fact]`, `[Theory]`, or direct tests of
  `OnPremiseClient`, `ADAuthClient`, `ClaimsBasedAuthClient`, or `WsdlLoader`
  were found.
- F02 is gate-blocked by the authoritative map.
- Required future provider gate: Dataverse client build/tests.
- Required future consumer gates: F03A/F03Q compile and host compile.
- None of those commands were run because this diagnosis prohibits all
  restore/build/test and generated-output writes.
- Quarantine: false.

## Project Lifecycle Notes

`PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj:4`
targets `net10.0`. With SDK default compile globs and no exclusion, the bundled
NSspi C# files are part of the main project even though the current
`NET7_0_OR_GREATER` authentication branch uses `NegotiateAuthentication`.

`PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj:3-14` is a separate,
solution-excluded legacy package definition targeting `netstandard2.0;net40`,
generating packages, signing with the committed key, and naming
`License.txt`. No such license file exists beside the project. This is a
dormant lifecycle artifact; it is not treated as a current runtime Critical.

## Explicit Exclusions

- ChurchReport entity names, FetchXML/QueryExpression shape, N+1 business
  queries, and business authorization.
- F03A CRUD/query/list/attachment operations.
- F03Q mixed CRM/LINE facade behavior.
- X01 DI registration, pool sizing, credential configuration, and host
  disposal policy except as read-only reachability evidence.
- X02C profiling behavior.
- Product source, projects, configuration, tests, solution, workflow, map,
  task files, other diagnostic workspaces, and existing generated output.

## Manifest Self-Check

- All 62 tracked F02 files are included.
- Dependencies and consumers were read only.
- Query/business behavior remains with F03A or ChurchReport owners.
- Existing `PowerPlatform.Dataverse.Client/obj/**` content predates this
  diagnosis and was not touched.
- Nested agent count: 0.
