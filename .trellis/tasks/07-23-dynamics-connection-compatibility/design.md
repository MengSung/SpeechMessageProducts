# Design: profile-isolated Dynamics Access Gateway and embedded host

## 2026-07-29 hosting and compatibility amendment

The current recommendation is Central Gateway for production plus Local Gateway for Visual Studio/development or an explicitly isolated deployment. Both are deployments of the same `Gateway` execution mode and differ by `Gateway.Endpoint`; the current enum remains `Gateway | Embedded`.

Embedded is not removed, but further Embedded rollout is deferred. CE 8.2 and CE 9.1 share the product-facing REST/operation contract, not one universal transport, SDK version, authentication state, or physical connection pool. Microsoft official SDKs are permitted only behind Gateway adapters or process-isolated compatibility workers. The third-party Data8 client remains a temporary CE 8.2 IFD bridge and must not become the permanent Gateway pool foundation.

Where the older no-SDK-only sections conflict with this amendment, use `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` as the executable contract and `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` as the explanatory decision record.

## 1. Decision

Add a new Dynamics integration project group to the existing
**SpeechMessageProducts.sln** solution. Do **not** create a mandatory separate
**SpeechMessage.Dynamics.sln** as the default implementation boundary. The
deployable ASP.NET Core service, **SpeechMessage.Dynamics.Gateway**, remains
the default production path to Dynamics 365. A product may instead select a
supported **Embedded** host adapter in its own deployment JSON when it needs
local Visual Studio debugging/testing or a deliberately isolated deployment.
The selection is made only at startup by trusted deployment configuration; it
is never selected by an end user, a LINE ID, a browser session, or a request.

The gateway contains a private no-SDK OData v4 connector library,
**SpeechMessage.Dynamics.WebApi**. Product applications call the gateway's
versioned REST contract over the internal network. They do not reference
Microsoft CRM SDK assemblies, Dataverse Client, the connector library, or
Dynamics credentials.

A separate **SpeechMessage.Dynamics.sln** may be introduced later only as an
optional build/deployment slice or solution filter equivalent when operations
evidence shows that it is useful. It is not required for Phase 0 or Phase 1,
and it must not become a second source of truth for project references.

This is deliberately a **two-host, one-core** design:

1. The Gateway is the shared operational/security boundary for five current
   products and anticipated future products.
2. The private WebApi library is the low-level, independently testable,
   direct-HTTP implementation used by both approved hosts. Products never
   reference it directly; the embedded adapter is the only supported in-process
   entry point.
3. The Gateway API and Embedded adapter expose the same controlled Organization
   operation abstraction, not a transparent proxy of arbitrary CRM/OData URLs.
4. Gateway and Embedded hosts keep separate process-local socket pools but use
   one shared organization-admission coordinator whenever they target the same
   validated Dynamics organization.

This is the recommended architecture for the stated five-to-ten product
scenario. It is not a blanket rule that every Dynamics integration needs a
microservice.

## 2. Evidence and option comparison

### 2.1 Local evidence

| Evidence | Consequence |
| --- | --- |
| ChurchReport Startup registers one singleton ICrmConnectionPool from one CRM configuration. | It cannot safely own simultaneous per-product/per-version profile runtimes. |
| CrmConnectionPool stores URL, username, password, and IOrganizationService; CrmConnectionService creates OnPremiseClient. | The current pool is a SOAP client pool, not a profile-isolated HTTP runtime pool. |
| OnPremiseClient implements IOrganizationService and uses WSDL/WS-Trust/WCF. | The borrowed project is not a direct Web API implementation. |
| The current solution contains a direct Microsoft.Crm.Sdk.Proxy HintPath and Microsoft.Xrm type coupling. | The user-requested no-SDK end state requires an explicit migration boundary, not a package update. |
| Current configuration contains secret material. | Duplicating a library into five products would multiply secret and operational risk. Secrets must be rotated during migration. |

### 2.2 Options

| Option | Benefits | Decisive drawbacks | Decision |
| --- | --- | --- | --- |
| A. Each product copies/references a low-level direct-HTTP library and talks to CRM. | Lowest hop count; simple for one or two co-deployed products. | Each product still needs CRM credential delivery, profile configuration, HTTP connection state, token state, retries, metadata cache, audit, and 8.2/9.1 guardrails. Five-to-ten independently managed copies increase leak and drift risk. | Reject. Products must not reference the low-level library directly. |
| B. One generic transparent CRM/OData proxy. | Centralizes outbound HTTP connections. | Lets callers express arbitrary tables, URLs, queries, headers, and potentially profiles. It leaks CRM schema/control, expands the attack surface, and makes authorization/audit non-deterministic. | Reject. |
| C. One controlled Gateway backed by a private direct Web API library. | One secret boundary, one profile-runtime owner, consistent version detection, centralized telemetry, per-product authorization, and a stable contract for future products. | Adds a network hop and requires availability/operations discipline. | **Recommend.** Deploy it internally with a highly available topology; preserve low-level testability in the private library. |
| D. A supported Embedded host adapter selected by one product's JSON. | Visual Studio can debug the connector in-process; an isolated product deployment may remove the Gateway hop. | The product becomes a runtime host and must satisfy the identical secret, operation, lifecycle, audit, admission, and smoke-test gates; its process-local pool cannot bypass organization-wide capacity. | **Allow as a controlled exception.** It is not a copied connector or per-user pool, and `Gateway` remains the default production mode. |

### 2.3 Why the recommendation is evidence-backed

- Microsoft documents CE/Dataverse Web API as OData v4 and states that there is
  no required language-specific assembly, so a direct HTTP connector is a
  supported implementation strategy.
- Microsoft documents that each HttpClient owns a connection pool. If five
  products each manage direct CRM access, they create independently managed
  credential/handler/pool/cache lifecycles. A central gateway reduces those
  lifecycle owners to one implementation with a profile-scoped pool.
- The existing code proves that this repository already has one single-profile
  pool and SDK/SOAP coupling. It is not a reusable multi-product isolation
  boundary.

The Gateway is therefore selected as the default for centralization of *security
and runtime state*, not because a microservice is fashionable. Embedded mode is
intentionally narrower: it keeps the same implementation and safety controls
while changing only the host location. A product cannot use it to invent a new
pool, bypass admission, or retain user-specific Dynamics state.

## 3. Existing solution topology

~~~text
SpeechMessageProducts.sln
- existing product projects
- SpeechMessage.Dynamics.Abstractions
  - profile-neutral interfaces, records, errors, capability model
- SpeechMessage.Dynamics.WebApi
  - direct HTTP/OData v4 connector and profile runtime pool
- SpeechMessage.Dynamics.Gateway
  - ASP.NET Core internal REST service, authorization, operations, health
- SpeechMessage.Dynamics.Embedded
  - approved in-process host adapter with the same runtime/operation contract
- SpeechMessage.Dynamics.Tests
  - unit, contract, fake-server, isolation, pool-lifecycle tests
- SpeechMessage.Dynamics.SmokeTests
  - opt-in authenticated 8.2/9.1 environment verification harness
~~~

`SpeechMessage.Dynamics.Embedded` is the only supported in-process host adapter.
It references the no-SDK core internally and is deliberately distinct from
`SpeechMessage.Dynamics.WebApi`, so products cannot consume arbitrary OData
transport APIs.

The new Dynamics projects are registered in the existing root solution when
implementation begins, which keeps Visual Studio 2026 development, test
discovery, dependency visibility, and eventual SDK-removal scans in one project
graph. The Gateway remains independently deployable because deployment is
defined by project publish/container artifacts, not by a separate `.sln` file.
No product project gets a project reference to SpeechMessage.Dynamics.WebApi. A
product either uses the Gateway OpenAPI/HTTP contract or references only
SpeechMessage.Dynamics.Embedded; it never receives a direct low-level
transport, generic CRM API, or SDK type.

### 3.1 Ownership rules

| Project | Owns | Must not own |
| --- | --- | --- |
| Abstractions | Product-neutral request/result records, profile capabilities, error codes, connector interfaces. | Microsoft.Xrm types, Entity, OrganizationRequest, SDK packages, concrete credentials. |
| WebApi | HTTP request composition, authentication handlers, profile validation, metadata/capability cache, the profile runtime pool, pagination/batch/action transport. | ASP.NET endpoint authorization, product business rules, user session state. |
| Gateway | Inbound workload authentication, product-to-alias policy, request validation, controlled operation dispatch, audit/metrics/health, lifetime orchestration. | Raw CRM credentials in request/config response, arbitrary URL forwarding, product-specific domain rules. |
| Embedded | The same controlled-operation dispatch and runtime host inside one product, plus strict product-mode JSON admission and local Visual Studio hooks. | A copied WebApi implementation, caller-selectable routing, per-user/LINE pools or sessions, bypassing the shared organization-admission coordinator. |
| Tests/SmokeTests | Deterministic fake endpoints and opt-in real environment checks. | Committed secrets, real production writes by default. |

## 4. Target data flow

~~~mermaid
flowchart LR
  P1["Product A..N<br/>trusted product JSON + workload identity"] --> M{"Execution mode"}
  M -->|Gateway| G["Dynamics Access Gateway<br/>capability + alias authorization"]
  M -->|Embedded| E["Embedded host adapter<br/>same controlled operation contract"]
  G --> R["Profile Runtime Pool<br/>key: profile + config generation"]
  E --> R
  R --> W["No-SDK Web API Connector<br/>HttpClient + OData v4"]
  W --> C82["CE 8.2<br/>/api/data/v8.2/"]
  W --> C91["CE 9.1<br/>/api/data/v9.1/"]

  S["Secret provider"] --> R
  MC["Profile metadata / capability cache"] --> R
  A["Organization admission coordinator<br/>epoch + host slot + queue budget"] <--> R
  O["Metrics, audit, health"] <---> G
  O <---> E
  O <---> R
~~~

Each deployed Gateway or Embedded process has its own physical socket pools; TCP
connections are not shared across hosts. The configuration, secret references,
operation policy, and organization-admission plan are centrally governed, but
profile runtime objects are process-local and disposable. The admission
coordinator is shared precisely so local socket pools cannot multiply Dynamics
concurrency.

### 4.1 Product execution-mode selection

Every product composes the same `IOrganizationOperationsClient` abstraction,
but deployment selects one host implementation:

~~~text
Gateway mode:  Product -> authenticated Gateway REST adapter -> Gateway -> WebApi
Embedded mode: Product -> Embedded host adapter              -> WebApi
~~~

The choice is declarative and fail-closed. It is not a feature flag evaluated
per user or per request, and changing it creates a new host/runtime generation
through a restart or controlled replace-and-drain. Both paths execute only an
approved capability operation and both participate in the exact same
`OrganizationAdmissionKey` coordination when they target one Dynamics
organization. Thus Embedded mode means local socket pooling and local
debugging, not unlimited private CRM capacity.

#### Product-owned JSON contract

Each product has a separate versioned JSON document, normally its
`appsettings.{Environment}.json`, containing only non-secret integration shape.
It is parsed by a duplicate-property-aware parser before normal options binding.
Exactly one mode branch is permitted.

Gateway mode example:

~~~json
{
  "$schema": "https://schemas.speechmessage.local/dynamics-access-product.v1.schema.json",
  "DynamicsAccess": {
    "SchemaVersion": 1,
    "ExecutionMode": "Gateway",
    "WorkloadSubjectId": "church-report-service",
    "Gateway": {
      "Endpoint": "https://dynamics-gateway.internal/",
      "OrganizationAlias": "membership"
    }
  }
}
~~~

Embedded mode example:

~~~json
{
  "$schema": "https://schemas.speechmessage.local/dynamics-access-product.v1.schema.json",
  "DynamicsAccess": {
    "SchemaVersion": 1,
    "ExecutionMode": "Embedded",
    "WorkloadSubjectId": "church-report-service",
    "Embedded": {
      "ProductProfileBinding": "church-report-membership",
      "OrganizationAdmissionCoordinatorRef": "dynamics-admission-production"
    }
  }
}
~~~

`WorkloadSubjectId` is a bounded deployment-assigned workload identifier, not a
user, LINE ID, JWT subject, CRM user, or browser session. `Gateway.Endpoint` is
an allowlisted internal service URI, not a CRM URI. `ProductProfileBinding`
selects a deployment-provisioned, server-owned profile definition; endpoint,
credentials, secret references, API version, capability registry, and admission
budget remain outside product JSON. The inactive branch, unknown fields,
duplicate fields, raw CRM URL, raw credential/token, user identity/session
field, and any dynamic mode override fail validation.

The JSON document is not trusted as an authorization source. Gateway mode derives
the workload subject from the authenticated internal service principal and
verifies the requested alias against the central product-profile registry.
Embedded mode must load a signed deployment manifest or verify its
`ProductProfileBinding` and `OrganizationAdmissionCoordinatorRef` against the
same central registry before resolving secrets, profile runtime state, or
organization-admission slots. A local edit to `WorkloadSubjectId`, binding, or
coordinator reference cannot grant a product access to a different organization.
If that manifest/registry lookup is unavailable, times out, is stale, or fails
signature/policy validation, Embedded startup fails closed and remains NotReady;
it never trusts local product JSON as authorization truth.

Embedded trust artifacts are explicit and auditable:

- a signed manifest contains schema version, product ID, workload subject,
  permitted product-profile binding, permitted admission coordinator reference,
  allowed environment, expiry, monotonic version, and signing key ID;
- registry verification returns the same fields plus revocation state and policy
  decision version;
- the trust anchor is deployment-owned, signing keys rotate through overlapping
  key IDs, and stale/rollback manifests are rejected by monotonic version and
  expiry checks;
- a bounded registry cache may preserve a previously valid non-revoked decision
  only until its TTL; timeout beyond TTL, explicit revocation, invalid signature,
  or policy denial keeps Embedded NotReady.

For Visual Studio development, `appsettings.Development.json` may select
`Embedded` with a fake CRM endpoint/profile fixture or `Gateway` with a local
Gateway process. The development profile must be explicitly non-production and
must fail startup if it resolves a production secret reference or production
organization identity. This makes in-process debugging observable without
teaching a workstation to retain a production CRM connection/session.

Embedded fake-profile development does not bypass the trust boundary. It uses a
separate development trust anchor with either an approved local development
registry or a signed Development manifest. That artifact may authorize only the
Development environment, a fake/local endpoint allowlist, and a designated
non-production organization identity; it cannot validate a production profile,
production secret reference, production registry, or production signing key. A
missing/unreachable development registry, invalid signature, expired manifest,
or fake-profile policy mismatch leaves Embedded NotReady exactly as production
does.

## 5. Controlled product-facing API

The public gateway surface is versioned, JSON-only, and based on an OpenAPI
document. Product callers authenticate with their workload identity. The
authorization policy maps the authenticated workload plus a product-visible
organization alias to exactly one server-owned profile generation.

Representative endpoints:

| Endpoint | Purpose | Constraint |
| --- | --- | --- |
| POST /v1/organizations/{alias}/operations/{capabilityOperationId} | Execute one pre-registered query, read, command, action, or function through its typed named-parameter envelope. | The alias and capability operation ID select a fixed server-owned CRM template/projection/page/write rule; callers cannot supply a CRM schema target, CRM action/function identifier, filter grammar, raw OData URL, or arbitrary headers. |
| GET /v1/health/profiles/{alias} | Operator diagnostic health only. | Requires a separately authorized operator workload identity/policy and is unavailable to ordinary product identities; redacts all secrets and identity details. |

The API does not accept an outbound URI, profile name, auth header for Dynamics,
password, token, FetchXML text/fragment/flag, or an unbounded batch payload.
If a registered capability uses FetchXML internally, its fixed server-owned
template accepts only typed bounded named parameters; the caller never supplies
any FetchXML text. A product alias is a logical name such as
"membership"; it is not a CRM hostname.

Every operation template is compiled from metadata-typed parameter definitions,
not string concatenation. FetchXML builders must use XML attribute/text encoders
for values and a fixed allowlist for entity, attribute, relationship, operator,
and ordering tokens. OData URL/filter builders must use OData literal and URI
component encoders for values. A typed parameter value can never be inserted into
an XML, OData, URL, or multipart boundary context without the context-specific
encoder declared by the operation definition.

For the first production release, the operation registry is authoritative:
capabilities such as member.get, member.search, list.addMembers, and
metadata.optionSet map to a fixed OData template, projection, maximum page
size, approved action, and named parameter schema. The service exposes no
generic query endpoint. It must never recreate unrestricted
IOrganizationService.Execute under another name.

Phase 0 must create an Organization-call coverage matrix before the first
consumer migration. Required columns are: product, file/member, legacy SDK/SOAP
entry point, current `OrganizationRequest`/helper shape, data classification,
proposed `capabilityOperationId`, Web API route/action/function template,
v8.2/v9.1 metadata evidence, real-server smoke-test evidence, idempotency/audit
classification, migration status, owner, and removal deadline. Any row without
an approved Web API capability remains a temporary legacy item; it cannot be
handled by a generic Execute proxy.

The Gateway must not become the location for business workflows. For example,
ChurchReport still decides its own donation or membership business rule; it asks
the gateway for an approved Organization operation.

## 6. Configuration and safe version detection

### 6.1 Profile configuration

JSON describes non-secret shape and names of secrets only. It is loaded by the
Gateway; product applications never receive the profile configuration.

~~~json
{
  "DynamicsGateway": {
    "SchemaVersion": 1,
    "Profiles": {
      "church-ce82-prod": {
        "OrganizationBaseUri": "https://crm82.example.internal/Contoso/",
        "ExpectedApiVersion": "v8.2",
        "ExpectedOrganizationId": "11111111-1111-1111-1111-111111111111",
        "Authentication": {
          "Mode": "Windows",
          "CredentialSource": "HostIdentity"
        },
        "Runtime": {
          "MaxConnectionsPerServer": 4,
          "RequestTimeoutSeconds": 60,
          "PooledConnectionLifetimeMinutes": 15,
          "MetadataCacheMinutes": 30
        }
      },
      "church-ce91-prod": {
        "OrganizationBaseUri": "https://crm91.example.internal/Fabrikam/",
        "ExpectedApiVersion": "v9.1",
        "ExpectedOrganizationId": "22222222-2222-2222-2222-222222222222",
        "Authentication": {
          "Mode": "AdfsOAuth",
          "AuthoritySecretName": "DYNAMICS_CE91_ADFS_AUTHORITY",
          "ClientIdSecretName": "DYNAMICS_CE91_ADFS_CLIENT_ID",
          "FeasibilityEvidenceId": "ce91-adfs-service-flow-validated",
          "CredentialReferenceName": "DYNAMICS_CE91_ADFS_CREDENTIAL"
        },
        "Runtime": {
          "MaxConnectionsPerServer": 4,
          "RequestTimeoutSeconds": 60,
          "PooledConnectionLifetimeMinutes": 15,
          "MetadataCacheMinutes": 30
        }
      }
    },
    "OrganizationAdmissions": {
      "11111111-1111-1111-1111-111111111111": {
        "AggregateMaxInFlight": 24,
        "MaximumRuntimeHosts": 6,
        "LocalQueueCapacity": 48,
        "MaxDispatchEnvelopeBytes": 65536,
        "QueueAdmissionTimeoutSeconds": 15,
        "AdmissionDrainTimeoutSeconds": 90
      },
      "22222222-2222-2222-2222-222222222222": {
        "AggregateMaxInFlight": 24,
        "MaximumRuntimeHosts": 6,
        "LocalQueueCapacity": 48,
        "MaxDispatchEnvelopeBytes": 65536,
        "QueueAdmissionTimeoutSeconds": 15,
        "AdmissionDrainTimeoutSeconds": 90
      }
    },
    "WorkloadPolicies": {
      "church-report": {
        "Aliases": {
          "membership": "church-ce82-prod",
          "reporting": "church-ce91-prod"
        },
        "Capabilities": [
          "contact.read",
          "contact.write",
          "list.members.read"
        ]
      }
    }
  }
}
~~~

Actual environment values must come from an approved secret provider, such as
deployment secrets, Windows credential facilities/gMSA where appropriate, or an
enterprise secret store. Migration rotates all current credentials before this
configuration becomes active.

`OrganizationBaseUri` is a normalized absolute HTTPS URI, not merely an origin.
It may contain the approved Dynamics organization/virtual-directory base path
(for example, `https://contoso:8080/Test/`) but cannot contain user-info, query,
or fragment. The validator derives a separate `OrganizationOrigin` and exactly
one `ApprovedWebApiRoot = OrganizationBaseUri + api/data/{ExpectedApiVersion}/`.
It never strips a configured base path. Every credential-bearing request,
service-document probe, metadata probe, and returned `nextLink` must remain
under that exact approved Web API root.

`OrganizationAdmissions` is keyed by the canonical expected organization GUID.
Every profile resolves exactly one entry using its `ExpectedOrganizationId`; a
profile cannot override its aggregate/runtime-host/local-queue/admission-drain
values.
This removes duplicated shared capacity settings from profiles and makes a
missing or mismatched organization entry a configuration failure.

Windows authentication is a strict tagged union, not a mixture of optional
fields:

~~~json
{
  "Mode": "Windows",
  "CredentialSource": "HostIdentity"
}
~~~

`HostIdentity` means the runtime host uses the Windows service/IIS identity, gMSA, or
validated Linux Kerberos host identity; username/password/domain secret fields
are prohibited. A separately approved service-account mode is explicit:

~~~json
{
  "Mode": "Windows",
  "CredentialSource": "SecretReference",
  "UserNameSecretName": "DYNAMICS_CE82_USERNAME",
  "PasswordSecretName": "DYNAMICS_CE82_PASSWORD",
  "DomainSecretName": "DYNAMICS_CE82_DOMAIN"
}
~~~

`SecretReference` is allowed only for a non-human service account and resolves
those values from the secret provider. `HostIdentity` and `SecretReference` are
mutually exclusive, and neither mode permits plaintext values in JSON.

`AdfsOAuth` is a third strict shape: it permits only an authority reference,
client-ID reference, target-specific `FeasibilityEvidenceId`, and a credential
reference for an already-proven service workload. It rejects
`UserNameSecretName`, `PasswordSecretName`, `DomainSecretName`, raw `Password`,
ROPC fields, `ClientSecret`, and certificate/private-key fields. This is an
explicit feasibility gate, not a claim that every CE IFD installation supports
a noninteractive flow. Before an IFD profile is admitted, its evidence record
must prove a cold start against the exact target: issuer/audience, non-password
service/workload mechanism, `WhoAmI` service identity, expiry/renewal behavior,
and the absence of browser cookie, user password, refresh-token, and user-session
persistence. Otherwise the profile remains unavailable.

### 6.1.1 Strict configuration admission

`LocalMaxInFlight` is a derived runtime value, not an independently tunable JSON
field:

~~~text
LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumRuntimeHosts)
~~~

The profile validator rejects the entire replacement generation before it can
become Ready unless all of the following are true:

- `AggregateMaxInFlight >= MaximumRuntimeHosts >= 1`, so the derived local
  admission limit is at least one;
- `MaxConnectionsPerServer` is an integer from one through
  `LocalMaxInFlight`; every outbound attempt, including a retry, first holds a
  local outbound-work lease;
- `LocalQueueCapacity` is finite, non-negative, and no larger than the
  deployment's hard per-host/per-organization queue cap; `MaxDispatchEnvelopeBytes`
  is a positive manager-owned value no larger than the deployment cap, and every
  typed envelope is rejected before queueing if its canonical serialized size
  exceeds it. A queued request's remaining deadline must still be positive
  before it is admitted;
- every profile resolves the single `OrganizationAdmissions` entry for its
  `ExpectedOrganizationId`; its aggregate/runtime-host limits, local queue
  capacity, envelope byte limit, queue-admission deadline policy, and
  admission-drain timeout cannot be overridden at profile level;
- each `OrganizationAdmissions` entry has a named operational owner, measurement
  date, CE target/version, approved Gateway host maximum, approved Embedded host
  maximum, HPA/IaC maxima, rollout policy, and fairness policy. CI compares the
  deployment/IaC maxima to this artifact before a profile can become Ready;
- request timeout, pool lifetime/idle timeout, cache TTL/size, retry count, and
  drain timeout are positive bounded values within deployment-owned limits;
- a Gateway-dependent production deployment reserves at least two of the
  `MaximumRuntimeHosts` slots for Gateway replicas, and IaC enforces at least
  two ready-capable Gateway hosts in the same organization-capacity domain.
  Embedded host counts (including their deployment replica limits) are included
  in `MaximumRuntimeHosts` before any profile becomes Ready;
- `OrganizationBaseUri` is an absolute HTTPS URI with an approved normalized
  base path and without user-info/query/fragment; the version is exactly `v8.2`
  or `v9.1`; secret fields are references only; and unknown/duplicate
  configuration is rejected in every environment.
- the root configuration and each product-mode configuration have a supported
  `SchemaVersion`; a duplicate-aware streaming JSON reader rejects duplicate
  property names before deserialization, then a schema validator rejects unknown
  fields, missing required fields, inactive mode branches, invalid normalized
  URIs, raw secrets, and unsupported schema versions. Normal object binding by
  itself is not accepted as proof of duplicate-field safety.
- each authentication object conforms to its exact tagged-union shape:
  `Windows/HostIdentity`, `Windows/SecretReference`, or the separately validated
  AD FS OAuth shape. Fields from another credential source, including password,
  client-secret, and certificate/private-key fields in `AdfsOAuth`, are rejected.

These checks make a malformed capacity setting an availability failure rather
than a path to excess CRM concurrency, unbounded queue retention, or a
permanently blocked request.

### 6.2 Version policy

Production routing is **explicit configuration first**, with detection as a
validation mechanism:

1. An operator defines the expected base URI, organization identity, auth mode,
   and expected API route as v8.2 or v9.1. When exact CE product-release proof
   is required, onboarding also records the documented Discovery-service
   instance/release version; the Web API route alone is not treated as product
   release evidence. The Discovery release record is a one-time
   operator/onboarding artifact outside solution source and cannot introduce
   SDK-generated code or Dynamics SDK DLL dependencies.
2. A provisioning validator resolves secrets and sends only read-only probes to
   the configured exact `ApprovedWebApiRoot`: service document, CSDL metadata,
   and a permitted identity/health operation such as WhoAmI.
3. The validator records observed organization identity, configured API route,
   endpoint, metadata ETag/version, and the feature capability matrix. Service
   document/CSDL/WhoAmI prove authenticated route/capability readiness, not the
   CE release number; this distinction is required because v8.x routes became
   identical after the 8.2 upgrade.
4. Any configured-route mismatch, unavailable metadata, wrong organization, unsupported
   authentication, or feature mismatch marks the profile unavailable. It does
   not silently route to another version or another organization.
5. An optional administrative "AutoDetect" helper can probe a fixed,
   allowlisted candidate set on the configured host during onboarding. Its only
   result is a report requiring a configuration/deployment change; it never
   mutates a live profile or chooses a runtime route.

This design meets the desire for intelligent detection without converting
route/capability validation into an unsafe cross-organization router or falsely
claiming an unverified CE product release.

### 6.3 Authentication feasibility gates

| Profile mode | Intended use | Gate |
| --- | --- | --- |
| Windows | CE on-premises Active Directory/IWA. | Prefer `HostIdentity` (Windows service/IIS/gMSA or validated Linux Kerberos). `SecretReference` is a separate service-account-only mode. Validate hosting OS and network/IWA support against the real target. Handler and credentials remain profile-owned. |
| AdfsOAuth | CE on-premises IFD/claims environment. | Require target-specific cold-start proof of a non-password service/workload flow, issuer/audience, expected `WhoAmI`, expiry/renewal, and no browser/user-session persistence. Do not fall back to ROPC or the old WS-Trust SDK client. |
| DataverseOAuth | Future Dataverse/online profile. | Separate future mode using supported OAuth/MSAL and an application/user identity. It is not claimed as CE 8.2/9.1 client-secret support. |

CE on-premises client-secret/certificate client-credentials support must not be
promised or silently attempted. It is a Dataverse-only capability in the
official guidance used for this design. If a target IFD cannot prove a
non-password AD FS OAuth service flow suitable for a service workload, that
profile is blocked until its identity infrastructure changes; neither Gateway
nor Embedded host stores an end-user password or silently uses ROPC.

For a Windows/IWA profile, Phase 0 must record one approved deployment mode:

- runtime host hosted on Windows with the required service identity/IIS or service
  configuration; or
- runtime host hosted on Linux only after the exact Kerberos/keytab setup passes
  an end-to-end CE 8.2/9.1 IWA smoke test in the target-like environment.
  Windows gMSA is a Windows-host identity option, not a synonym for Linux
  Kerberos/keytab hosting.

Without one of these proofs the Windows profile is unavailable, its runtime host
is not ready for it, and the project cannot advance to production implementation
for that profile.

## 7. Profile runtime pool and zero-tolerance isolation

### 7.1 Correct meaning of "Connection Pool"

Each approved runtime host owns a **DynamicsProfileRuntimePool**, not a manual
pool of borrowed HttpClient instances. A profile runtime owns one long-lived
SocketsHttpHandler plus HttpClient and therefore its actual underlying HTTP/TCP
connection pool. It also owns authentication, metadata, health, and its
generation-scoped retry/circuit state. Organization-wide admission is owned by
the runtime pool manager, separately from the credential-bearing runtime.

Each credential-bearing runtime is keyed by:

~~~text
ProfileRuntimeKey = tuple(
  profileId, immutableConfigurationGeneration, apiVersion,
  normalizedOrganizationBaseUri, authMode, secretVersionFingerprint)
~~~

The key never contains raw secret values. A request can only resolve a runtime
after Gateway authorization or Embedded startup policy resolves the deployment
assigned workload and authorized logical alias. No user, LINE ID, browser
session, raw JWT, or user token is a runtime-key component.

The runtime key is intentionally **not** the capacity key. A configuration reload
can overlap an old and a new runtime generation while the old one drains; using
the generation key for both would allow the same organization budget to be
admitted twice. Every runtime host therefore resolves a non-secret canonical
capacity key from validated deployment metadata:

~~~text
CanonicalOrganizationCapacityKey = tuple(
  expectedOrganizationId,
  normalizedOrganizationBaseUri)

OrganizationAdmissionKey = OrganizationAdmissions.AdmissionNamespaceId
RuntimeHostSlotLeaseNamespace = OrganizationAdmissions.LeaseNamespaceId
~~~

`CanonicalOrganizationCapacityKey` excludes generation, endpoint, auth mode,
secret version, product identity, caller session, and raw deployment-environment
label. It owns one `OrganizationAdmissions` entry for the same physical
Dynamics organization. `OrganizationAdmissionKey` is the entry's approved
admission namespace used by queue/permit code; it is never constructed ad hoc
from a profile's raw environment string. `RuntimeHostSlotLeaseNamespace` is the
entry's approved durable lease namespace for the coordinator and may include
environment labels only when they have already been bound to the canonical
capacity entry. The entry scopes one local bounded queue/semaphore plus optional
distributed permits across *all* aliases and runtime generations that target the
same validated Dynamics organization:
`AggregateMaxInFlight`, `MaximumRuntimeHosts`, `LocalQueueCapacity`,
`MaxDispatchEnvelopeBytes`, queue-admission deadline policy, and
admission-drain timeout are manager-owned, not profile-local overrides. An
expected organization identity is immutable for a logical profile;
moving to another organization requires a new profile/alias and explicit policy
cutover, never an in-place generation replacement.

Separate v8.2 and v9.1 profiles may intentionally target the same expected
organization during an upgrade/migration window. They retain separate
credential/HTTP/metadata runtime generations but resolve the same
`OrganizationAdmissionKey` and `OrganizationAdmissions` entry, so the
organization's capacity never doubles merely because two API-version profiles
exist.

The same rule applies across deployment-environment labels. If two profiles in
different `deploymentEnvironment` values resolve to the same physical Dynamics
organization by `ExpectedOrganizationId` and/or the same normalized organization
base URI, startup fails unless a single explicitly approved cross-environment
`OrganizationAdmissions` entry binds both labels to one shared admission budget.
This prevents an accidental test/staging/production label split from doubling
traffic against one CRM organization.

For implementation, separate the names:

- `CanonicalOrganizationCapacityKey` is derived from the validated physical
  organization identity and normalized organization base URI. It owns the single
  aggregate concurrency/queue/memory budget.
- `RuntimeHostSlotLeaseNamespace` is the durable slot namespace used by the
  coordinator. It may include deployment-environment labels for isolation, but
  every namespace that maps to the same canonical capacity key must reference the
  same `OrganizationAdmissions` entry or fail startup.
- `OrganizationAdmissionKey` in queue/permit code is resolved from the approved
  capacity entry, not constructed ad hoc from a profile's raw environment string.
  There is no code path where `tuple(deploymentEnvironment, expectedOrganizationId)`
  becomes an independent queue, semaphore, permit-limiter key, lease namespace,
  or budget.

### 7.1.1 Canonical composite-key encoding

The tuple notation above and every later multi-field key is **not** string
concatenation. In-memory maps use typed records with structural equality. A key
that crosses a process/store boundary uses `CanonicalKeyV1`:

~~~text
bytes = ASCII(kind) + 0x00
      + for each fixed-order field:
          ASCII(fieldName) + 0x00 + UInt32BigEndian(UTF8(value).length) + UTF8(value)
externalKey = "v1." + base64url(bytes)
~~~

`kind`, field order, field names, maximum UTF-8 byte length, and value
normalization are fixed per key type. Length prefixes make delimiters, Unicode,
and field-boundary collisions impossible; raw secret values are never key
components. Changes require a new key version rather than a silent encoding
change.

### 7.2 Runtime object ownership

Handlers use `UseProxy = false`; the connector must not inherit an ambient
system/env proxy or send CRM credentials through an undeclared proxy. A proxy is
out of scope for the initial design and requires a separately reviewed,
profile-owned trust and credential boundary if ever introduced. The connector
never writes caller data or Dynamics authorization to
`HttpClient.DefaultRequestHeaders`; it creates a one-use `HttpRequestMessage`
and attaches only server-owned OData/auth headers for that request.

| State | Isolation rule | Lifecycle |
| --- | --- | --- |
| SocketsHttpHandler and HttpClient | One per profile runtime key. UseCookies is false; AllowAutoRedirect is false; normal TLS validation is mandatory; no handler is shared across profile generations. `PreAuthenticate` remains disabled unless target-like Windows/IWA tests compare the disabled baseline with enabled behavior, prove correct connection-bound authentication/no cross-profile signal, and show a measured benefit for that exact profile. | The runtime owns both objects and creates `HttpClient` with `disposeHandler: true`; no factory or caller shares/disposes the handler. It is created once, reused for requests, drained, then disposed exactly once. PooledConnectionLifetime is configured to handle DNS/network changes. |
| Windows credentials/host identity | Assigned only to that runtime's handler. `HostIdentity` uses the already validated process/service identity; `SecretReference` resolves only a non-human service account for that generation. | Host identity has no secret copy to dispose; secret-derived credentials are discarded at generation disposal. |
| OAuth token provider/cache | Separate per profile key and authority/client scope; no static/global token dictionary. A keyed asynchronous single-flight operation allows exactly one token refresh per cache key. | Bounded in memory; clear/dispose on generation retirement. A caller's cancellation stops only its wait, not shared refresh work. Only completion or runtime-drain cancellation removes the keyed entry using its attempt identity; no plaintext token persistence by default. |
| Metadata/capability cache | Key includes profile runtime key and metadata ETag/version. A keyed single-flight operation prevents metadata stampedes. | Size/TTL bounded; explicit invalidation after deployment/schema signal; disposed with runtime. A caller may abandon its wait but cannot remove a shared in-flight refresh; completion/drain removes the matching attempt and cannot leave an unbounded dictionary. |
| Retry/circuit state | Separate per profile runtime key. | Bounded and cancellation-aware; removed with runtime. |
| Organization admission state | One bounded local queue/semaphore per `OrganizationAdmissionKey`, shared by Gateway and Embedded hosts plus old/new runtime generations and aliases for the same validated organization. Its optional distributed permit key is the same non-secret key. Queue entries contain a bounded typed envelope with a server-derived `WorkloadSubjectId`, authorized alias, immutable capability operation revision/hash, deadline, and policy-decision version; they never retain HttpContext, principal/JWT, headers, cookies, streams, credentials, a user/LINE identity, or a generation reference. Cancellation removes an undispatched entry atomically, and bounded per-workload fair scheduling prevents one workload from filling the organization queue. | Reference-counted by active generations; removed only after the last one drains. The active runtime is resolved after dequeue, but it may execute only the queued immutable operation revision; a changed/missing policy or operation revision rejects the item rather than rebinding it to a new semantic template. Aggregate queue capacity and payload size are calculated across all admitted runtime hosts. |
| Request context | Immutable local request object carrying an opaque generated correlation ID, server-derived `WorkloadSubjectId`, authorized alias, and operation revision only. | Never placed in a singleton, static, AsyncLocal, or shared cache. Correlation IDs and metric/audit tags must not encode a user, LINE ID, JWT/session ID, CRM user, or credential. |
| Warm-up state | One low-priority, keyed single-flight task per profile runtime generation. It contains only the profile key, a system warm-up subject, and bounded readiness probe state. | It performs bounded service-document/CSDL cache population and a read-only `WhoAmI` probe through the same audit reservation, admission, runtime-host lease, deadline, and cancellation gates as ordinary work. It is cancelled on drain/lease loss and never retains user/login identity. |
| Per-request HTTP objects | The connector owns every HttpRequestMessage, HttpResponseMessage, HttpContent, and response stream; no transport object crosses the Gateway contract boundary. | Dispose on success, error, cancellation, and drain. Only bounded parsed DTOs or controlled streamed copies may escape the transport layer. |

### 7.2.1 Aggregate organization budget across runtime hosts

A local per-host limiter alone is insufficient. `OrganizationAdmissions`
declares one capacity plan for every Gateway and Embedded process that can
reach the same validated organization:

~~~text
AggregateMaxInFlight = safe budget for the target Dynamics organization
MaximumRuntimeHosts = maximum concurrent Gateway + Embedded runtime hosts
LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumRuntimeHosts)
~~~

The validator requires `AggregateMaxInFlight >= MaximumRuntimeHosts >= 1` and
derives `LocalMaxInFlight`; it does not accept an independent local value. It
also requires `1 <= MaxConnectionsPerServer <= LocalMaxInFlight`, a finite
deployment-capped local queue, and a global worst-case bound of
`MaximumRuntimeHosts * LocalQueueCapacity * MaxDispatchEnvelopeBytes`. Every
deployment/HPA maximum for Gateway and Embedded hosts is counted before the
plan is published. This fixed allocation is the safe fallback when the
distributed limiter is unavailable: at most
`MaximumRuntimeHosts * LocalMaxInFlight <= AggregateMaxInFlight` outbound
requests can be admitted, even during a dependency outage.

Phase 1 deliberately uses equal-weight host allocation because it is predictable,
auditable, and safe before real production load measurements exist. A later
`HostRoleWeights` extension may allocate different local limits to Gateway,
Embedded, blue/green, or canary roles only when the weights live in the
`OrganizationAdmissions` artifact, CI proves the weighted sum cannot exceed
`AggregateMaxInFlight`, and the no-distributed-limiter fallback remains bounded.

When an optional `OrganizationPermitLimiter` distributed permit limiter is
introduced, it is keyed only by
`OrganizationAdmissionKey` and supports a bounded server-derived workload
fairness dimension. It issues bounded permits before local admission and never
contains tokens, credentials, cache objects, or caller session data. The
process-local admission controller is shared across overlapping generations,
and every host uses the same coordinator, so an old/new drain or a
Gateway/Embedded combination cannot double the organization budget. If the
distributed limiter fails, every host returns to its fixed conservative local
allocation; it never becomes unbounded. Capacity values are derived from real
8.2/9.1 service-protection/load measurements and reviewed whenever host count or
product load changes.

Queue fairness is a configured algorithm, not an implementation afterthought.
The first release must enforce per-workload queue caps plus deficit/weighted fair
dispatch with an aging bound; a global FIFO that lets one product fill the
organization queue is rejected. Rejection order is deterministic: expired
deadline, oversized envelope, stale/unauthorized operation revision,
per-workload queue cap, then organization cap. Metrics expose queue share,
starvation age, reject reason, and per-workload wait time.

### 7.2.2 Runtime-host admission enforcement and fencing

`MaximumRuntimeHosts` is enforced twice, rather than treated as a deployment
convention. `IRuntimeHostSlotCoordinator` must provide atomic conditional
create/renew/fenced-release semantics from a shared durable coordinator;
process-local memory, best-effort heartbeats, and clocks without coordinator
authority are not acceptable. The coordinator stores a globally monotonic
`AdmissionEpoch`, slot TTL, expiry-fence margin, and the quarantine deadline for
each expired/revoked slot. The deployment selects and fault-tests its coordinator
backend before any production profile becomes Ready:

Before Phase 2 implementation starts, an ADR must select the durable
coordinator/ledger/audit backend. It must specify the store, transaction model,
clock source, fencing-token semantics, acquire/renew/release operations,
idempotency create-or-read operation, audit reservation transaction,
TTL/quarantine formulas, outage/fail-closed behavior, backup/restore behavior,
and deterministic test harness. Process-local memory, best-effort leases,
unbounded in-memory retry queues, and sticky Gateway sessions are rejected.

1. Infrastructure-as-code and CI reject the sum of Gateway and Embedded
   deployment/HPA maxima when it exceeds the required organization plan.
2. Before reporting readiness, each runtime host acquires a short renewable
   `RuntimeHostSlotLease` for every enabled
   `RuntimeHostSlotLeaseNamespace` at the current `AdmissionEpoch`. The
   namespace is resolved only from the canonical `OrganizationAdmissions` entry
   and is shared by every blue/green/canary revision and embedded product host
   mapped to that physical organization. The lease
   contains no CRM secret, token, cache, request session, user identity, or LINE
   ID.
3. A local outbound-work lease may be admitted only when its declared maximum
   lifetime (request deadline + bounded cleanup/cancellation margin) ends before
   the current slot's expiry-fence time. `RuntimeHostSlotLeaseTtl` must be at
   least the maximum outbound-work lifetime plus that margin. A retry requires
   a new work lease and must pass this test again.

If no slot is available, the new host remains NotReady and receives no traffic.
A single renewal RPC error is not itself a loss of a still-valid
coordinator-issued lease: retry with bounded backoff only within the current
lease TTL. A **LeaseFailure** occurs when the coordinator explicitly rejects or
revokes a lease, its epoch is fenced, or the TTL reaches expiry before renewal
succeeds. At that point every affected host immediately closes admission for new
outbound CRM work and retries and reports NotReady. It cancels any work not
completed by the expiry-fence deadline; no process may extend a lease locally or
allow a work lease to survive expiry. The coordinator does not reissue an
expired/revoked slot until its quarantine interval (maximum outbound-work
lifetime plus network-cancellation settlement margin) has elapsed or the old
host has durably acknowledged all work leases at zero. This deliberately favors
a bounded outage over an aggregate CRM concurrency spike.

Graceful termination and rolling update follow the same capacity rule. On a
termination signal, an instance first becomes NotReady and closes new CRM
admission, but retains its `RuntimeHostSlotLease` while its already leased work
drains. It calls fenced release only after active outbound-work leases reach
zero and the runtime is disposed. Releasing before drain would let a new host
admit a full local budget while the old one is still using capacity.
Infrastructure CI rejects rollout policies that wait for a surge host to become
Ready before a slot holder can terminate; use a capacity-aware handoff (for
example, zero surge with one controlled unavailable host) or equivalent
orchestration that preserves the aggregate budget.

### 7.2.3 Coordinated configuration and secret epochs

An organization admission plan is not changed independently in each process.
The durable coordinator publishes an immutable `AdmissionEpoch` containing the
organization key, aggregate budget, maximum runtime-host count, queue limits,
slot TTL/fence/quarantine values, and a configuration revision digest. A host
may become Ready only after it has validated and acknowledged the current epoch.

For a capacity/configuration change, the coordinator first publishes a pending
epoch with `min(oldCapacity, newCapacity)` as the effective budget. Hosts stop
new admission under the old epoch, acknowledge/drain or are fenced, then acquire
slots under the new epoch. A host presenting an old epoch is NotReady and cannot
obtain new work permits. A confirmed credential revocation immediately fences
the affected profile generation and prevents grace-period traffic; an ordinary
replacement-validation failure may use only the separately approved last-valid
credential window. This barrier prevents a blue/green or staggered secret reload
from combining old and new local allocations into excess CRM concurrency.

### 7.3 Drain and configuration reload

Configuration refresh must use replace-and-drain:

1. Validate the new profile configuration and current coordinator
   `AdmissionEpoch`, then build one new generation without publishing it.
   Per logical profile, at most one active and one draining generation may
   exist. Rapid updates are serialized/coalesced; a third generation is not
   created until the drain completes or the prior generation is cancelled at its
   declared deadline.
2. Atomically switch new request resolution to the new generation only after
   the current epoch acknowledges its safe capacity/configuration digest.
3. Mark the previous generation as draining and stop its generation-bound
   dispatch/retry callbacks. The `OrganizationAdmissionManager` owns the only
   shared organization queue, so it remains available for new work to resolve
   the new active generation after dequeue. A queued item that cannot safely
   rebind to an active compatible generation **with the identical immutable
   operation revision/hash** is rejected before contacting CRM; policy recheck
   may narrow/deny access but may not change its server-owned CRM template.
   Existing outbound-work leases may finish only before their runtime-host
   expiry fence and normal deadline.
4. Wait for in-flight work up to the configured shutdown timeout, honouring
   request cancellation. Do not create a new retry, token refresh, or metadata
   fetch after drain begins.
5. Cancel/await background loops, warm-up, and single-flight owners, dispose all response
   objects/streams, cancellation registrations, timers, tokens, handlers, and
   the HttpClient exactly once, then clear generation-owned cache references and
   record final metrics. The shared organization queue is removed only when the
   last compatible generation releases its reference.
6. If draining exceeds the deadline, cancel the remaining work, await its
   teardown, and record an operational alert; do not leave an orphaned
   generation or callback.

No runtime object is changed in place. This eliminates the class of bug where a
new endpoint or credential partially mutates an active profile or rapid reloads
accumulate unbounded draining handler/token-cache generations.

### 7.4 Secret-version change detection

Each profile has a non-secret secret-version stamp supplied by the secret
provider. Each runtime host subscribes to provider change notification where
available and otherwise polls only the version stamp at a bounded interval. A
version change initiates the same validate -> publish -> drain workflow as a
configuration change; it never overwrites an active credential in place.

Every Gateway and Embedded runtime host must consume the same change through the
coordinator's `AdmissionEpoch`. Until a replacement generation validates, the
last valid generation remains active only within its approved credential grace
period and only when the coordinator has not fenced it. After expiry or a
confirmed revocation, new admission fails closed and raises an alert. Raw
secret/token values are never used as cache keys or emitted by the watcher.

This credential-validity continuity window is unrelated to, and never relaxes,
the RuntimeHostSlotLease no-admission-after-lease-loss rule in section 7.2.2.

### 7.5 Zero-tolerance release gates

"Zero tolerance" is a release policy, not a claim that a computer can prove a
universal absence of future defects. Any of the following is a release blocker:

- a fake-server test observes Profile A's credential/header/token/cookie or
  endpoint on a Profile B request;
- a request is routed based on caller-provided host/credential/unapproved
  profile input;
- a Gateway or Embedded host reaches Dynamics without the current admission
  epoch, a valid runtime-host slot, or the expiry-fence/quarantine protocol;
- a queue/audit/cache/metric/correlation value contains a user identity, LINE
  ID, JWT/session identifier, user token, or credential;
- a retired profile runtime retains active timers, handlers, response streams,
  cancellation registrations, or references after the drain/dispose test;
- a controlled soak test demonstrates sustained unbounded managed heap,
  handler, socket, or queue growth after warm-up;
- telemetry contains a secret, bearer token, raw authorization value, or
  unredacted sensitive identity detail.

## 8. Web API transport behavior and compatibility

### 8.1 Common behavior

- Use direct HTTPS HTTP requests with OData v4 headers and JSON payloads.
- Every outbound Web API request sets request-scoped authorization only and
  defaults to `Accept: application/json`, `OData-Version: 4.0`, and
  `OData-MaxVersion: 4.0`; requests with JSON bodies set
  `Content-Type: application/json`. The connector may set `Prefer` only from an
  allowlist approved by the operation capability, such as `return=representation`,
  `odata.include-annotations`, or `odata.maxpagesize`; caller-supplied `Prefer`
  headers are never forwarded blindly.
- Each registered capability declares finite `MaxRequestBytes`,
  `MaxResponseBytes`, `MaxPageCount`, and `MaxPageBytes`. The connector rejects
  an excessive Content-Length before reading and enforces the same byte limit
  while streaming a chunked response; it never buffers an unbounded body or
  page into memory.
- Automatic decompression and ambient `Accept-Encoding` are disabled for the
   first release. A response with any `Content-Encoding` is rejected with a
   typed unsupported-content-encoding error before it is parsed; the connector
   must never accidentally interpret compressed bytes as JSON/XML. If a later
   profile enables an allowlisted compression algorithm, it must first pass
   real-target throughput/CPU/p95 measurement and then use bounded streaming
   decompression with a decompressed-byte limit, expansion-ratio limit, finite
   nesting/encoding-chain policy, and malformed-content rejection before parsing.
   The default remains disabled when those measurements do not show a safe net
   benefit.
- Resolve approved schema targets/actions/functions from each profile's CSDL
  metadata rather than assuming an online Dataverse schema/capability. Apply
  finite `MaxServiceDocumentBytes` and `MaxMetadataBytes` limits to both
  declared and streamed responses. Parse CSDL with DTD/external-entity
  resolution disabled and bounded XML document depth/character/name counts;
  cache only the validated bounded model.
- Use server-driven paging through the returned nextLink; never synthesize
  skip-based paging. Resolve a relative link only against the runtime's exact
  `ApprovedWebApiRoot`; an absolute link must be HTTPS, match the validated
  organization origin **and** approved organization base path, and remain under
  the configured exact API-version root before the connector sends credentials
  or follows it. Any other link is a profile fault, not a URL to fetch.
- Use controlled FetchXML only after policy and URL-length checks. Use a
  controlled batch fallback for long requests. FetchXML is generated only by the
  server-owned template/builder using metadata-approved names and
  context-specific XML encoders; no typed parameter can change XML structure,
  close an element/attribute, inject an operator, or alter entity/attribute names.
- Use multipart batch support only with fixed operation count/size limits,
  robust response parsing, and transactional changesets where required.
- Respect Retry-After for 429 and use bounded transient retry/circuit-breaker
  logic for safe/idempotent work. A non-idempotent command needs an explicit
  idempotency strategy before retry.

### 8.2 8.2 versus 9.1 compatibility

| Capability area | CE 8.2 | CE 9.1 | Design requirement |
| --- | --- | --- | --- |
| API root | /api/data/v8.2/ | /api/data/v9.1/ | Explicit profile path; never automatically upgrade the version. |
| Version behavior | After a server upgrade to 8.2, the v8.0/v8.1/v8.2 service paths became identical; this does not make a v8.2 profile interchangeable with v9.1. | v9.x can contain version-specific, potentially breaking differences. | Maintain an explicit v9.1 route, separate capability profile, and real-server smoke tests; never infer support from another v9.x release. |
| Custom actions with mixed complex/simple return types | Officially known Web API gap. | Addressed from v9.0. | Capability validator rejects/flags affected 8.2 action requests. |
| Access-sharing operations added in v9.0 | Do not assume present. | Available where metadata confirms. | Capability allowlist and metadata check before exposing operation. |
| FetchXML response shape | Treat as profile-specific; do not infer a response projection from current Dataverse documentation. | Treat as profile-specific; do not infer a response projection from another v9.x release. | Map/normalize at the contract boundary only after explicit metadata and real-server compatibility tests. |

The Gateway starts with the operations needed by the migrated products. It does
not claim feature parity by implementing a generic SDK replacement.

## 9. Security, availability, and observability

### 9.1 Inbound service security

- Use service-to-service workload identity, such as an enterprise JWT/OIDC
  bearer identity plus mutually authenticated client certificate, according to
  the deployment platform. A validated certificate/SPIFFE identity or JWT
  issuer/audience/client ID is the identity source; an X-Product header or
  request-body claim is never trusted.
- Authenticate before alias resolution. Map identity to product and product
  capability policy server-side. The caller asks for a capability/logical alias,
  not a physical profile.
- Derive a bounded deployment/workload `WorkloadSubjectId` from that mapping and
  use it for queue fairness, policy recheck, and redacted audit attribution. It
  is not a raw JWT claim, end-user identity, LINE ID, CRM user, browser session,
  or user token. Embedded mode obtains the same identifier from signed/trusted
  startup configuration rather than an HTTP request.
- Enforce least privilege at both levels: CRM service identity permissions and
  Gateway product capability permissions. Use separate least-privilege Dynamics
  identities/profiles where product separation matters. Per-user Dynamics
  authorization is a separate explicitly designed feature; do not forward a
  user token or caller-supplied impersonation header by default.
- Rate/concurrency limits apply to workload plus organization admission key so
  one product cannot exhaust the shared CRM organization, regardless of Gateway
  versus Embedded host mode.
- Never log a complete query payload when it can contain PII. Audit operation
  name, policy result, profile alias/redacted identifier, correlation ID,
  outcome, latency, retry count, and error category.

### 9.2 Availability

- Run at least two Gateway replicas once the shared Gateway becomes a production
  dependency. Embedded deployments are separately sized, but every Gateway and
  Embedded process is a counted runtime host in the same organization admission
  plan. Hosts are stateless except for process-local profile runtimes.
- Readiness must fail for a required profile that has not passed validation,
  including expected organization ID/API-version mismatch, without exposing
  credential diagnostics to callers. The mismatch is an explicit critical
  profile state and produces a sanitized operational alert.
- Health checks distinguish process health, secret resolution health, profile
  readiness, CRM reachability, and degraded/circuit-open state.
- A product receives a typed retryable/non-retryable error; it does not receive
  raw CRM/AD FS internal details.

### 9.3 Metrics and alerts

Per profile generation, record request count, active requests, queue wait,
handler/socket lifecycle, connection reuse, latencies, status codes,
Retry-After waits, retries, circuit state, metadata-cache hit rate, allocation
rate, GC heap trend, and drain/disposal completion. Redact identity and secret
values. Alert on any configuration generation that fails to drain, expected
organization/version mismatch, secret-version replacement failure, or
profile-isolation guard failure.

Audit and telemetry retention is bounded by a documented data-classification
policy: retain only redacted operational fields for a fixed configured duration,
enforce maximum event payload/queue size, and delete/expire records through a
verified retention job. Raw CRM request/response bodies, access tokens,
authorization headers, cookies, and PII-rich URLs are prohibited from audit
storage.

Audit storage has a hard entry/byte quota and a required durable audit intent.
Before any outbound CRM request, the runtime atomically creates a bounded
`AuditIntent(Reserved)` with reservation ID, reserved byte count, redacted
operation identity/revision, and terminal-state capacity. For ledger-dependent
writes, creation of `IdempotencyLedger(Pending)` and `AuditIntent(Reserved)` is
one durable transaction; if the storage design cannot make that atomic, the
operation is unsupported rather than relying on an in-memory ordering guess.
Immediately before dispatch the intent becomes `Dispatching`; a confirmed
result transitions its intent and ledger to terminal `Completed`/`Failed` in a
durable ordered transaction. A crash, cancellation, or transport failure after
the dispatch boundary is recovered as `OutcomeUnknown` with a retained audit
intent, never deleted/reused or silently replayed. Reservations are released
only through a durable terminal/recovery transition, so a crash cannot leak
capacity or lose an audit record after CRM traffic.

If an intent cannot be created, the runtime rejects the operation before CRM
traffic. A retention-job failure immediately raises an alert and increases a
failure metric. At the audit high-water mark, new reservations are throttled;
at the hard limit they fail closed with a typed `AuditStorageUnavailable` error.
There is no unbounded in-memory retry queue or silent audit-event drop. Warm-up
uses the same low-priority audit-intent path. Non-audit telemetry has its own
bounded queue and may drop/summarize low-priority events with a visible drop
metric, never by retaining them indefinitely.

Telemetry uses an allowlisted adapter rather than automatic raw HTTP capture.
Before any exporter receives an event, it removes/redacts `url.full`, query
strings, headers, request/response bodies, and exception-object serialization.
Organization-operation responses and errors always send
`Cache-Control: no-store, private`; no output-cache middleware or shared
response cache may apply to those routes.

The idempotency ledger is not an audit log or a profile runtime cache. For
operations that cannot use a Dynamics alternate-key/upsert semantic, it is a
durable shared store so two Gateway or Embedded hosts cannot execute the same
write after a retry/failover. Its atomic identity is:

~~~text
tuple(
  workloadSubjectId, logicalProfileId, expectedOrganizationId,
  capabilityOperationId, idempotencyKey)
~~~

This tuple uses `CanonicalKeyV1`. The shared store implements atomic
create-or-read for this exact identity across all runtime hosts. Each record
also stores the immutable `OperationDefinitionRevision`/template hash and the
request fingerprint. An equal idempotency identity with a different operation
revision is a typed `IdempotencyOperationRevisionConflict`; it must never
rebind to a newly deployed CRM template or execute again. The store retains only
a fingerprint hash and a minimal redacted outcome/resource reference under a
fixed, bounded TTL; it never stores a raw token, request body, credentials, or
unbounded response. If the durable ledger cannot be reached or cannot atomically
record/read the identity before a ledger-dependent write, the runtime fails the
write before outbound CRM traffic rather than relying on sticky sessions or
blindly replaying it.

An idempotency key is a bounded-format, 1–128-character URL-safe value. The
ledger enforces per-workload and global entry/byte quotas before CRM dispatch;
quota exhaustion is a typed rejection, never a reason to evict a live key early.
Its fingerprint is a versioned HMAC-SHA-256 digest of the canonical bounded
typed parameter envelope **including the immutable operation revision**, not a
raw request body. A different fingerprint for the same identity is a typed
conflict.

The HMAC key is a separate secret-provider reference from CRM credentials. Each
ledger record retains only its non-secret fingerprint-key version. A key rotation
keeps verification material for the previous version through the complete ledger
retention window and only removes it after the coordinator confirms every host
uses the new key and no retained record needs the old verifier. If required
verification material/KMS access is unavailable, ledger-dependent writes fail
closed before CRM dispatch; they are never accepted without fingerprint
verification. A key-compromise response quarantines affected retained records
for reconciliation rather than silently treating them as valid. No HMAC key or
raw digest input enters telemetry.

The ledger state machine is fail-safe for uncertain external outcomes. It
atomically records `Pending` before dispatch; a duplicate with a different
fingerprint fails, while an equal duplicate returns the recorded result, an
in-progress result, or a typed `OutcomeUnknown` result. A confirmed CRM outcome
transitions to `Completed`. If cancellation, process loss, or transport failure
occurs after dispatch and before a confirmed response, the entry is retained as
`OutcomeUnknown`; it is never automatically replayed. An expired `Pending`
attempt is also converted to `OutcomeUnknown` by bounded recovery work, not
deleted for reuse. Only an operation backed by a CRM alternate-key/upsert (or
another proven CRM idempotency semantic) may automatically retry after an
uncertain outcome. The fixed retention TTL must cover the published caller retry
window plus the maximum reconciliation window.

## 10. Performance strategy

Performance comes from controlled reuse and bounded work:

- one long-lived handler/HttpClient per profile generation, not per request;
- PooledConnectionLifetime chosen for expected DNS/load-balancer change rate;
- finite MaxConnectionsPerServer, maximum in-flight work, and a bounded
  per-organization-admission queue/semaphore shared across Gateway and Embedded
  hosts;
- metadata/capability cache with ETag/TTL instead of repeated discovery;
- response streaming/read-as-headers only where the application can safely
  consume/dispose it;
- server-driven paging and controlled batch sizes; no unbounded batch;
- operation-specific timeout budgets and cancellation propagation;
- write retry only with an operation-specific idempotency design. Prefer a CRM
  alternate-key/upsert semantic where available; otherwise a bounded
  durable cross-replica idempotency ledger stores request fingerprint and a
  redacted result for its fixed retention period. A write is never blindly
  replayed.
- no blocking synchronous I/O or synchronous-over-async bridge in connector code.

#### Safe warm-up and login latency

The connection pool is warmed by service lifecycle, not by retaining a user's
CRM session. When a validated profile generation becomes Ready, it schedules one
low-priority, bounded, service-identity-only warm-up through the normal
`OrganizationAdmissionManager`: service document, bounded CSDL metadata cache,
and read-only `WhoAmI`. The warm-up is single-flight per profile generation,
does not run while normal queue pressure is above the configured threshold, and
is cancelled on drain, host-slot fencing, or audit/admission failure.

An incoming login can observe or wait for that already-running keyed warm-up for
only its bounded login deadline; it cannot create a user-keyed warm-up, supply
credentials, extend a connection lifetime, or store an account password, LINE
ID, user token, browser cookie, or CRM session in the pool. With Windows/IWA,
the warmed connection authenticates only the Gateway/Embedded service host
identity. With IFD, warm-up is disabled until the target-specific noninteractive
service-flow feasibility proof described in section 6.1 exists. A warm socket is
a latency optimization, never a readiness, authorization, or identity promise.

The throughput goal is **maximum safe sustained throughput**, not the largest
temporary burst. A request can enter the connector only after product policy,
global/replica admission, local outbound-work admission, and its remaining
deadline have all passed. Retries consume a new outbound-work lease. This makes
the fast path cheap after warm-up while preventing retries, queues, or a slow
organization from turning into unbounded memory or socket pressure.

Gateway and Embedded hosts must establish a pre-production baseline using the
real 8.2 and 9.1 servers. Initial acceptance targets, measured after warm-up
and excluding the CRM server's own execution time, are:

- profile lookup and authorization p99 under 1 ms in the relevant host process;
- Gateway-added overhead p95 under 5 ms and p99 under 15 ms on the deployment
  network; Embedded-added overhead is measured separately against direct Web
  API and must not weaken admission or lifecycle checks;
- no outbound discovery or metadata request on a warm normal CRUD call;
- no queue-induced thread-pool starvation or retained retired-generation object
  at 80% of the validated profile concurrency; and
- bounded queue depth and no thread-pool starvation at 80% of configured
  per-organization concurrency;
- a profile's slow/failing CRM server must not reduce another profile's
  configured concurrency or cache correctness.

Targets may only be relaxed with a documented real-server constraint, never by
removing isolation or lifecycle guards.

## 11. Verification strategy

### 11.1 Deterministic automated tests

- Unit test profile-key construction, policy mapping, request validation,
  version/capability decisions, redaction, retries, single-flight token/metadata
  refresh, idempotency, aggregate-budget calculation, and bounded queue
  behavior.
- Use two fake CRM HTTP endpoints with distinct identity headers/tokens to
  prove a 5-product x 2-profile concurrent matrix never crosses endpoint,
  credentials, token cache, metadata cache, retry state, or correlation data.
- Test empty/malformed configuration, wrong organization ID, unsupported
  metadata/action, invalid auth mode, cancellation, 429 Retry-After, 503,
  timeout, response-stream failure, and bad batch multipart responses.
- Exercise repeated configuration reloads. Assert every old test handler,
  token provider, timer, cancellation registration, and runtime becomes
  disposed/unreachable after drain. Use disposal counters plus weak-reference
  sentinels in the test harness to prove that no retired-generation object is
  strongly retained after the declared drain deadline.
- Contract tests pin the Gateway OpenAPI schema and prove it cannot accept
  caller-supplied outbound host/authorization/profile escape hatches.
- Validate both product JSON modes with the versioned schema and a
  duplicate-aware parser: reject unknown/inactive-mode fields, duplicate keys,
  raw CRM endpoint/credential/user/LINE/session values, dynamic mode overrides,
  and a development profile that resolves a production secret/organization.
  Prove Gateway and Embedded adapters expose the same bounded capability
  contract but only Embedded may be constructed in-process.
- Test organization URLs with a virtual-directory base path, relative and
  absolute `nextLink` values, wrong base path/origin/version, and URI
  normalization. Prove that only links below `ApprovedWebApiRoot` are followed.
- Test a queued item across a policy/operation-registry rollout. It may execute
  only its original immutable operation revision/hash; a changed/missing
  revision and an idempotency retry under another revision fail with typed
  conflict rather than reaching CRM.
- Test durable audit intent and idempotency state transitions at every boundary:
  reserve, pending, dispatching, confirmed, cancellation, process crash,
  recovery, retention, and quota release. Prove audit capacity cannot leak or
  disappear after CRM dispatch and warm-up also uses bounded low-priority audit
  intent.

### 11.2 Soak, fault, and performance tests

- Run a representative multi-profile soak after warm-up and inspect managed
  heap, active handler count, active timer count, open connection count, queue
  depth, live cancellation-registration count, and allocation trend. Establish
  a post-warm-up baseline, then require the retired-generation counters to
  return to zero and the remaining live counters to remain within their declared
  bounds; any unexplained retained object or sustained growth fails release.
- Run profile reload/drain soak while requests are in flight; validate no stale
  generation receives a new request, at most active-plus-one-draining generation
  exists, and every retired generation disposes.
- Run multi-replica admission tests with and without the distributed limiter.
  Include Gateway plus Embedded runtime hosts; verify the total concurrent
  outbound calls never exceed AggregateMaxInFlight and degraded limiter behavior
  remains conservative.
- Run saturated lease-expiry/replacement tests: fill every allocation, lose one
  host lease, and attempt to start a replacement. Prove work is admitted only
  before its expiry fence, late work is cancelled, slot quarantine prevents an
  overlap spike, and the aggregate limit is never exceeded. Repeat with an
  admission-epoch/capacity/secret-revocation transition where old/new hosts are
  deliberately staggered.
- Run login-path warm-up tests for cold, already-warm, contention, cancellation,
  and lease-loss cases. Prove one service-identity single-flight warm-up is
  bounded/low-priority and no user/LINE ID/token/session appears in any pool,
  cache, queue, audit event, metric tag, or correlation ID.
- Inject a RuntimeHostSlotLease coordinator/renewal failure. Verify new CRM
  admission and retries stop immediately, readiness turns false, existing work
  is cancelled no later than its expiry fence, and the coordinator quarantines
  the old slot before a replacement can consume its allocation.
- Inject slow endpoint, DNS/connection failure, cancellation, token refresh
  failure, 429, 503, malformed metadata, Gateway restart, and Embedded-host
  restart. Verify blast radius remains inside the originating profile.
- Benchmark warm request throughput/latency against direct CRM baseline and
  publish the result with the deployment profile.

### 11.3 Real-server smoke tests

For each CE 8.2 and 9.1 target, execute a credentialed opt-in smoke harness:

1. Validate configured base URI/service document, exact Web API route, metadata,
   and expected organization. Record Discovery-service instance/release data
   separately when an exact CE product-release assertion is required; do not
   claim that CSDL/route validation alone proves the release number.
2. Run WhoAmI/authorized read-only identity check.
3. Read representative entity projections and paged data.
4. Run a safe test-only create/update/delete or approved sandbox operation.
5. Verify needed custom actions, FetchXML result mapping, and batch behavior.
6. Repeat acquire/release/reload calls and confirm no profile cross-talk or
   runtime accumulation.

No target profile is marked supported until these smoke tests pass.

## 12. Migration and SDK-removal boundary

### 12.1 Current migration blockers

- The current ChurchReport project has a direct external
  Microsoft.Crm.Sdk.Proxy DLL HintPath.
- The literal user-supplied absolute DLL root does not appear verbatim in the
  project file, but the actual HintPath at
  SpeechMessageProducts.ChurchReport.csproj lines 108-110 reaches an external
  path containing Dynamics 365 SDK DLL and
  Microsoft.CrmSdk.CoreAssemblies.9.0.2.52/lib/net462/
  Microsoft.Crm.Sdk.Proxy.dll. It is therefore a real violation of the intended
  no-SDK boundary, not a harmless string mismatch.
- ToolUtility.Tests has a Microsoft.CrmSdk.CoreAssemblies package reference;
  it is part of the final no-SDK removal scope even if it is not currently
  included in the root solution.
- ChurchReport and ToolUtility have Microsoft Power Platform Dataverse Client
  package dependencies and source-level Microsoft.Xrm / IOrganizationService
  coupling.
- The local PowerPlatform.Dataverse.Client project is GitHub-derived and WCF
  based.
- The root solution currently includes PowerPlatform.Dataverse.Client as a
  buildable project, and ToolUtility currently references that project. Both
  edges are final-removal blockers, not implementation details to preserve.
- Existing controllers/services/tests expose SDK entities and Organization
  Service abstractions. This is a substantial consumer migration, not a simple
  connection-string change.
- The inventory found roughly 200 SDK-importing source files, including
  metadata/option-set, Assign, SetState, ExecuteMultiple, marketing-list, and
  WebServiceConnector paths. The existing ICrmClient interface is SDK-shaped
  (Entity, ColumnSet, QueryBase, OrganizationRequest/Response) and must not be
  used as the new connector contract.
- ToolUtilityFactory is a static singleton initialized once by Startup, which
  is also unsuitable for named multi-profile state.

### 12.2 Phased rollout

The rollout names below map directly to the execution-plan phases to prevent
ambiguous sequencing: Foundation = Phase 1; Gateway/control plane = Phases 2-3;
Prove = Phase 4; First consumer and product-by-product migration = Phase 5;
Removal/enforcement = Phase 6. The durable coordinator/ledger/audit ADR and the
capacity artifact are Phase 0 prerequisites and must be accepted before Phase 2
begins.

1. **Foundation:** create the new solution, isolated direct HTTP connector,
   Embedded host adapter, and duplicate-aware product-mode schema with
   fake-server tests; make no changes to product traffic.
2. **Gateway and host control plane:** implement inbound workload policy,
   profile runtime pool, shared organization admission coordinator, secret
   resolution, health/metrics, controlled operations, and mode validation.
3. **Prove:** run 8.2/9.1 real-server smoke/performance/isolation tests in a
   non-production organization.
4. **First consumer:** migrate one bounded ChurchReport use case behind a
   feature flag/shadow comparison. Use Gateway mode for the production path and
   prove Visual Studio fake-server/local-Gateway and Embedded development modes
   without production secrets. Do not convert all IOrganizationService use sites
   in one change.
5. **Product-by-product migration:** replace SDK data operations with the
   controlled operation abstraction. Default production selection to Gateway;
   permit Embedded only after its host-count/admission/audit/secret/smoke gates
   are shown to be equivalent. Add operation capabilities only after
   metadata/version tests.
6. **Removal:** remove legacy product references, SDK types/packages, local
   PowerPlatform.Dataverse.Client project, WCF CRM code, and the external DLL
   HintPath. Rotate legacy credentials.
7. **Enforcement:** add CI scans that fail the build if banned SDK/DLL paths or
   packages reappear.
   The final scan must also fail if SpeechMessageProducts.sln still includes
   PowerPlatform.Dataverse.Client or if any project still has a ProjectReference
   to it.
8. **Per-migrated-product bypass gate:** once a workload/product path is
   migrated, CI/startup must fail that source root if it references
   `Microsoft.Xrm*`, `Microsoft.CrmSdk*`, `Microsoft.PowerPlatform.Dataverse*`,
   `IOrganizationService`, `ICrmConnectionPool`, `ToolUtilityFactory`,
   `ToolUtilityClass` CRM helpers, or raw CRM connection strings outside the
   temporary-legacy matrix. Coexistence is permitted only for named unmigrated
   legacy use cases with owner and deadline.

### 12.3 Final no-SDK gates

The final migration is accepted only when an explicit CI source-root manifest
(all production and test project directories resolved from the solution/project
graph, excluding only `.trellis`, `.ccg`, `docs`, and other approved historical
artifact roots) produces no production or test source/project matches. Do not
search the entire repository and then hide planning documents with a broad
allowlist:

~~~powershell
rg -n "Dynamics 365 SDK DLL|Microsoft.Crm.Sdk.Proxy.dll" --glob "*.csproj" --glob "*.props" --glob "*.targets" .
rg -n "Microsoft.Xrm|Microsoft.CrmSdk|Microsoft.Crm.Sdk|Microsoft.PowerPlatform.Dataverse.Client" --glob "*.csproj" --glob "*.vbproj" --glob "*.fsproj" --glob "packages.config" .
rg -n "IOrganizationService|OrganizationServiceProxy|DiscoveryServiceProxy" --glob "*.cs" --glob "*.vb" --glob "*.fs" --glob "*.csproj" --glob "*.vbproj" --glob "*.fsproj" .
~~~

Historical task/log documentation may be excluded only by that narrow
documented artifact-root list. A test project or copied code sample is not an
exception to the no-SDK end state.

## 13. Explicit non-goals

- Do not carry the CRM 2011 OrganizationData.svc/OData v2 endpoint forward.
- Do not make SOAP/WCF/WS-Trust a hidden fallback in the final no-SDK design.
- Do not publish CRM credentials or a generic CRM SDK-like API to products.
- Do not create or retain a per-user CRM connection/session/token pool keyed by
  account name, LINE ID, JWT/session ID, or browser login. Embedded mode is a
  product host, not an end-user connection mode.
- Do not make a profile shared merely because its API version matches another
  profile.
- Do not treat a successful WSDL/SOAP call as proof that Web API 8.2/9.1
  compatibility passed.

## 14. Sources

- Microsoft, [Use the Dynamics 365 Customer Engagement Web API](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/use-microsoft-dynamics-365-web-api?view=op-9-1)
- Microsoft, [Authenticate to Dynamics 365 Customer Engagement with the Web API](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/webapi/authenticate-web-api?view=op-9-1)
- Microsoft, [Use connection strings in XRM tooling](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/xrm-tooling/use-connection-strings-xrm-tooling-connect?view=op-9-1)
- Microsoft, [Dynamics 365 Customer Engagement Web API versions](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/webapi/web-api-versions?view=op-9-1)
- Microsoft, [Dynamics CRM 2016 Web API limitations (archived v8.x reference)](https://learn.microsoft.com/en-us/previous-versions/dynamicscrm-2016/developers-guide/mt628816(v=crm.8))
- Microsoft, [Discover the URL for your organization using the Web API](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/webapi/discover-url-organization-web-api?view=op-9-1)
- Microsoft, [Web API service documents](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/web-api-service-documents)
- Microsoft, [Compose HTTP requests and handle errors](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/compose-http-requests-handle-errors)
- Microsoft, [Execute batch operations using Web API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/execute-batch-operations-using-web-api)
- Microsoft, [Page results using the Web API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/query/page-results)
- Microsoft, [Retrieve data using FetchXML](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/retrieve-data)
- Microsoft, [Use Web API actions](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/use-web-api-actions)
- Microsoft, [Guidelines for using HttpClient](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- Microsoft, [Do not use the OData v2 endpoint](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/best-practices/business-logic/do-not-use-odata-v2-endpoint)
