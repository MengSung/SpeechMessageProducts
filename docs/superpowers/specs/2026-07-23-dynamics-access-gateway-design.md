# Dynamics 365 Access Gateway — Architecture SPEC

## Outcome

For five current products and an expected expansion to ten or more, build one
internal **Dynamics Access Gateway** as the default production path to Dynamics
365. It uses a private no-SDK .NET HTTP/OData v4 connector and supports CE
on-premises 8.2 and 9.1 through explicit, profile-driven configuration. A
product may select a supported **Embedded** host adapter in its own trusted JSON
configuration for Visual Studio development, testing, or a deliberately isolated
deployment. This changes the host location, not the connector/security contract.

This is not a generic CRM proxy. Product services authenticate to a controlled,
versioned REST API and request an authorized capability. The gateway decides the
physical CRM profile, validates its version/capabilities, owns its connection
pool and caches, and keeps Dynamics credentials private.

The first API release uses an operation registry: each capability maps to a
fixed query/command/action template and bounded named parameters. It exposes no
generic OData/filter endpoint or transparent CRM proxy.

Before any workload is migrated, Phase 0 must produce an Organization-call
coverage matrix for every current `IOrganizationService`, `OrganizationRequest`,
legacy SOAP/SVC, SDK helper, and CRM pool call site. Each row maps to exactly one
status: approved Web API capability with v8.2/v9.1 metadata and smoke evidence,
temporary legacy item with owner/removal deadline, or explicit out-of-scope.
There is no bulk "generic Execute" replacement.

Every product invocation has exactly this execution shape:

~~~text
POST /v1/organizations/{alias}/operations/{capabilityOperationId}
~~~

The caller sends only the logical alias, an approved capability operation ID,
and typed bounded parameters. It cannot send a CRM schema target, raw OData,
CRM action/function identifier, FetchXML text/fragment/flag, physical profile,
endpoint, credential, or Dynamics authorization header. An operation that uses
FetchXML owns a fixed server-side template; the caller never supplies it.

## Product-selectable host mode

Each of the five products owns a strict, versioned JSON configuration that
chooses one startup-only mode:

| Mode | Use | Rules |
| --- | --- | --- |
| `Gateway` | Default production mode. | The product sends its bounded operation to the authenticated Gateway REST API. |
| `Embedded` | Visual Studio debugging/testing or an intentionally isolated product deployment. | The product references only `SpeechMessage.Dynamics.Embedded`, which hosts the same approved no-SDK runtime and capability contract in-process. It cannot reference the low-level Web API transport directly. |

The mode is deployment controlled and validated before startup. It cannot be
chosen by a caller, LINE ID, user account, browser session, request field, or
feature toggle evaluated per request. `Embedded` has its own process-local
HTTP/socket pool, but it still joins the same organization-admission coordinator
and aggregate capacity plan as Gateway hosts. It is not a per-user connection
pool or a capacity bypass.

Representative non-secret product configuration:

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

Embedded mode uses the same schema but permits only a deployment-provisioned
binding and admission-coordinator reference:

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

Changing `ExecutionMode` creates a new host/runtime generation through
replace-and-drain. The inactive branch, duplicate/unknown fields, raw CRM URI,
credential/token, user/LINE/session field, dynamic override, or unsupported
schema version is rejected before binding. Development JSON may point to a fake
CRM fixture or local Gateway but must fail if it resolves a production secret or
production organization identity.

The product JSON is a startup binding document, not an authorization authority.
In Gateway mode, workload identity is derived from the authenticated internal
service principal and checked against the central product-profile registry; any
editable `WorkloadSubjectId` mismatch fails startup/request admission. In
Embedded mode, the binding/admission reference must be signed or verified against
the same central registry before any CRM secret, profile runtime, or queue slot is
resolved. If the signed manifest or central registry is unavailable, times out,
or verification fails, Embedded startup fails closed / remains NotReady; local
JSON is never sufficient authority to bind a production profile.
Visual Studio Embedded fake-profile testing still uses a separate development
trust anchor: an approved local development registry or signed Development
manifest may bind only a fake endpoint and non-production organization identity.
It can never validate a production binding, and its unavailability or invalid
signature also leaves Embedded NotReady.

The full technical design and implementation plan are maintained with the
planning task:

- [Detailed design](../../../.trellis/tasks/07-23-dynamics-connection-compatibility/design.md)
- [Execution plan](../../../.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md)

## Why this is the recommended boundary

The alternative of putting a shared connector library into every product is
better only for a small number of co-deployed applications with one trusted
credential/profile. It is not the best fit here because every product would
otherwise independently own CRM secrets, HttpClient socket pools, token caches,
metadata caches, retry behavior, telemetry, and 8.2/9.1 compatibility logic.

Microsoft documents that:

- Dynamics Customer Engagement Web API is OData v4 and can be called directly
  with HTTP; it does not require a language-specific SDK assembly.
- Each HttpClient has its own connection pool, so its lifetime and isolation
  must be deliberately managed.
- CE Web API behavior differs between the v8.x and v9.x lines and must not be
  treated as a silently interchangeable endpoint.

The current repository reinforces this decision: it has one singleton,
single-profile SDK/SOAP pool, direct Microsoft CRM SDK coupling, and a direct
external CRM SDK DLL HintPath. It is not safe to replicate that model across
five to ten products.

## Architecture

~~~mermaid
flowchart LR
  Products["5–10 Product Services"] -->|"mTLS + workload JWT"| Gateway["Dynamics Access Gateway"]
  Gateway --> Policy["Product / Capability / Alias Policy"]
  Products -->|"Embedded (trusted startup JSON)"| Embedded["Embedded host adapter"]
  Embedded --> Policy
  Policy --> Pool["Profile Runtime Pool"]
  Pool --> Connector["Private no-SDK Web API Connector"]
  Connector --> CE82["Dynamics CE 8.2"]
  Connector --> CE91["Dynamics CE 9.1"]

  Secrets["Secret Provider"] --> Pool
  Admission["Organization admission coordinator\nepoch + host slots + queue budget"] <--> Pool
  Telemetry["Audit, Metrics, Health"] <--> Gateway
  Telemetry <--> Embedded
  Telemetry <--> Pool
~~~

The existing **SpeechMessageProducts.sln** is planned to receive this new
Dynamics project group:

| Project | Responsibility |
| --- | --- |
| SpeechMessage.Dynamics.Abstractions | DTO-only contracts and error/capability abstractions; no CRM SDK types. |
| SpeechMessage.Dynamics.WebApi | Direct OData v4 transport, authentication, capability validation, and profile runtime pool. |
| SpeechMessage.Dynamics.Gateway | Internal REST API, workload authentication, authorization policy, operations, health and telemetry. |
| SpeechMessage.Dynamics.Embedded | The only supported in-process product host adapter; strict mode binding and the same controlled runtime/operation contract. |
| SpeechMessage.Dynamics.Tests | Isolation, lifecycle, contract, resilience, and performance tests. |
| SpeechMessage.Dynamics.SmokeTests | Opt-in non-production CE 8.2/9.1 verification. |

Products normally use the Gateway OpenAPI/HTTP contract. An embedded exception
references only the Embedded adapter, never the low-level connector; neither
mode receives CRM credentials or CRM SDK types.

Final no-SDK acceptance requires removing the current
`PowerPlatform.Dataverse.Client` project from `SpeechMessageProducts.sln`,
removing every `ProjectReference` to it, and deleting or moving the project out
of buildable source after consumers migrate. The project is temporary legacy
only; it must not be wrapped, renamed, or retained as the new connector.

A separate **SpeechMessage.Dynamics.sln** is not the default implementation
boundary. It may be added later only as an optional build/deployment slice or
solution-filter equivalent, and it must not become a second source of truth for
project references.

## Non-negotiable runtime rules

1. One immutable profile runtime generation in an approved Gateway or Embedded
   host owns one handler/HttpClient socket
   pool, credentials/token provider, metadata cache, rate/retry state,
   and health state.
2. Credential-bearing caches and runtime state use the immutable profile plus
   configuration generation, normalized organization base URI, API version, and auth context.
   A separate non-secret canonical organization capacity key, derived from the
   validated organization identity plus normalized organization base URI, scopes
   each concurrency budget across all aliases and old/new runtime generations for
   that organization. No unkeyed static/global state is allowed.
   If two configured deployment environments resolve to the same physical
   Dynamics organization by `ExpectedOrganizationId` and/or normalized
   organization base URI, startup fails unless one explicitly approved
   cross-environment `OrganizationAdmissions` entry merges their budgets.
   The admission budget is never keyed by the environment label alone; any
   environment-specific lease namespace must point to that one canonical capacity
   entry.
   Multi-field keys are typed tuples in memory and use one versioned,
   length-prefixed canonical encoding at process/store boundaries—never direct
   string concatenation.
3. Cookies and automatic redirects are disabled. A caller cannot choose an
   outbound CRM URL, Dynamics authorization header, physical profile, or
   impersonation header.
4. Configuration reload creates a validated new generation, atomically switches
   admission, drains the old generation, and disposes all handlers, streams,
   timers, registrations, and generation-owned cache/token state exactly once.
   The shared organization queue is reference-counted and disposes only after
   its last compatible generation drains.
5. The socket pool belongs to SocketsHttpHandler; the system must not hand
   pooled HttpClient/connection objects to callers.
   `UseProxy` is false, cookies/automatic redirects are disabled, and Dynamics
   authorization is attached to a one-use request message rather than a shared
   default header.
6. "Zero tolerance" means every observed cross-profile/session/token/credential
   leak, retained retired runtime, or sustained unbounded memory/resource growth
   blocks the release.
7. An organization admission plan declares an aggregate Dynamics concurrency
   budget. The configured maximum Gateway plus Embedded runtime-host count
   determines a fixed safe per-host allocation; a
   distributed permit service may improve fairness but its failure must fall
   back to that conservative allocation, never unlimited traffic.
   Phase 1 intentionally uses equal per-host allocation as the conservative
   default. Later host-role weights may be added only inside the
   `OrganizationAdmissions` capacity artifact and must preserve the aggregate
   budget under Gateway, Embedded, blue/green, and canary hosts.
   Infrastructure CI/HPA limits and a renewable RuntimeHostSlotLease both
   enforce the host maximum; an excess process remains NotReady.
8. If the coordinator rejects/revokes a RuntimeHostSlotLease, fences its
   admission epoch, or its TTL expires before bounded renewal succeeds, the
   host immediately stops new CRM admission/retries and becomes NotReady. Work
   may be admitted only when its full maximum lifetime fits before the lease
   expiry fence; remaining work is cancelled at that fence. The coordinator
   quarantines the expired/revoked slot before reuse for the maximum work
   lifetime plus settlement margin, so a replacement cannot overlap old work
   and exceed the aggregate budget. No local expiry extension/grace exists.
9. Configuration is rejected before readiness unless
   `AggregateMaxInFlight >= MaximumRuntimeHosts >= 1`, derived
   `LocalMaxInFlight` is at least one, and queue/connection/timeout values are
   finite and deployment-bounded. `MaxConnectionsPerServer` cannot exceed the
   derived local outbound-work limit.
   A Gateway-dependent production deployment additionally reserves at least two
   ready-capable Gateway hosts, while every Embedded deployment replica also
   counts toward `MaximumRuntimeHosts`.
10. A returned OData nextLink is followed only after it is validated as HTTPS,
    the same approved organization origin **and base path**, and the exact
    configured Web API root/version. No caller-supplied or cross-origin paging
    URL is ever fetched.
11. `OrganizationAdmissionManager` owns the only bounded organization queue.
    Entries are typed/bounded dispatch envelopes with a server-derived workload
    subject and immutable operation revision/hash. They retain no HttpContext,
    principal/JWT, headers, cookies, credentials, streams, user/LINE identity,
    session, or old-generation reference; dequeue may not rebind the operation
    to a changed semantic template.
    A canonical-organization-keyed `OrganizationAdmissions` JSON map owns one
    `AggregateMaxInFlight`/`MaximumRuntimeHosts`/`LocalQueueCapacity`/
    `MaxDispatchEnvelopeBytes`/deadline/drain setting set; profiles only
    reference it through `ExpectedOrganizationId` and cannot override it.
    Queue fairness is explicit: admission enforces a per-workload queue share
    and deficit/weighted fair dispatch with an aging bound, so one product cannot
    monopolize an organization's queue. Rejections occur before enqueue in this
    order: expired deadline, oversized envelope, unauthorized/stale operation,
    per-workload cap, organization cap.

## Compatibility and configuration

Production configuration requires an explicit expected API route (v8.2 or
v9.1), normalized organization base URI (including any CE virtual-directory
path), and expected organization identity. A discovery helper validates the
configured exact Web API root, metadata and identity, but it may not silently
select, upgrade, or fall back between profiles/versions. This proves configured
route/capability readiness, not the CE product release; when exact release proof
is required, onboarding records the Discovery-service instance/release data as a
one-time operator/onboarding artifact outside solution source. That record cannot
introduce a Dynamics SDK DLL or generated SDK dependency into the solution.

CE AD profiles use verified Windows/IWA connectivity. CE IFD profiles require a
target-specific cold-start proof of a non-password AD FS/OAuth service-workload
flow, correct issuer/audience, expected `WhoAmI` service identity, expiry/
renewal behavior, and no browser cookie/user password/refresh-token/session
persistence. The design does not
silently retain WS-Trust/SOAP, enable ROPC/store end-user passwords, or claim
Dataverse client-secret/certificate behavior for CE on-premises.

The gateway uses named JSON profiles with secret *references* only. Existing
secret material is rotated instead of being migrated into JSON. A secret-version
signal/poll publishes a new profile generation and drains the old one; it never
mutates an active credential in place.

A Windows/IWA profile is usable only after its exact hosting mode passes a
target-like smoke test: Windows service/IIS/gMSA identity, or explicitly
configured Linux Kerberos/keytab support. Otherwise that profile is unavailable.

`LocalMaxInFlight` is derived as
`floor(AggregateMaxInFlight / MaximumRuntimeHosts)`, rather than accepted as
an independent mutable setting. A canonical-organization-keyed
`OrganizationAdmissions` map owns aggregate/runtime-host/local-queue/envelope-
byte/admission settings; profiles resolve it through `ExpectedOrganizationId`
and cannot duplicate or override it. The profile validator uses a versioned schema plus duplicate-aware
pre-binding parse; it rejects unknown/duplicate fields, non-HTTPS or
credential-bearing base URIs, invalid versions, values outside deployment hard
caps, or queue entries whose request deadline has already expired.
`PreAuthenticate` is disabled by default and can be enabled only after
target-like Windows/IWA tests compare disabled/enabled behavior, prove correct
connection-bound authentication/no cross-profile signal, and show a measured
benefit for that exact profile.

The `RuntimeHostSlotLease` namespace is the approved
`RuntimeHostSlotLeaseNamespace` from the canonical `OrganizationAdmissions`
entry. All blue/green/canary and Embedded/Gateway hosts mapped to one canonical
capacity entry compete through that namespace rather than obtaining their own
unsafe replica limit. Its coordinator must support atomic conditional
create/renew/fenced-release; process-local counters or best-effort heartbeats are
not sufficient.

The implementation cannot start Phase 2 until an ADR selects the durable
coordinator/ledger/audit backend and records its atomic primitives. The ADR must
name the store, clock source, fencing-token semantics, acquire/renew/release
transactions, TTL/quarantine formulas, outage behavior, and test harness. A
process-local coordinator, best-effort heartbeat, or unbounded in-memory ledger is
not an acceptable backend.

On graceful termination, an instance first becomes NotReady and stops new CRM
admission but retains its slot until already leased outbound work drains and the
runtime disposes; it then atomically releases the slot. Infrastructure must use
a capacity-aware rolling-update handoff rather than require a surge replica to
become Ready while all existing slots are still held. A single renewal RPC error
does not invalidate a still-valid lease; fail-closed admission begins when the
coordinator rejects/revokes/fences the lease or its TTL expires before a bounded
retry succeeds. Work cannot outlive the expiry fence, and the coordinator
quarantines the slot before reuse. No local expiry extension is allowed.

Windows/IWA configuration is a strict tagged union: `HostIdentity` uses the
validated service/IIS/gMSA/Kerberos host identity and prohibits account-secret
fields; `SecretReference` permits only references for a non-human service
account. Mixed fields and plaintext credentials are rejected.

`AdfsOAuth` is a separate strict shape: only authority/client-ID/target-specific
feasibility-evidence/credential references are allowed. Password/ROPC/
client-secret/certificate/private-key fields are rejected and the proven
non-password service flow remains a target-environment feasibility gate.

## Performance and release gates

Performance is based on long-lived profile-owned HTTP connection reuse,
metadata-cache reuse, bounded concurrency/backpressure, pagination, controlled
batching, cancellation, and Retry-After behavior—not unlimited concurrency.

The performance objective is maximum **safe sustained** throughput: warm normal
calls must create no discovery/metadata request; profile lookup/authorization
targets p99 under 1 ms; Gateway-added latency targets p95 under 5 ms and p99
under 15 ms on the deployment network. Those targets are measured against the
real 8.2/9.1 baseline and may not be met by weakening isolation, lifecycle, or
Dynamics service-protection limits.

### Safe warm-up, not per-user pooling

When a validated profile generation becomes Ready, the host may start exactly
one low-priority, bounded, service-identity warm-up: service document, bounded
CSDL metadata cache, then read-only `WhoAmI`. It passes the same audit-intent,
organization admission, runtime-host lease, deadline, and cancellation gates as
normal work. It is cancelled on drain or lease fencing and is suppressed under
normal queue pressure. A login may only join that already-running single-flight
operation through the static product/profile binding; it cannot create a
user-keyed pool entry or store a password, LINE ID, user token, cookie, browser
session, or CRM session. For Windows/IWA it warms only the service host identity;
for IFD it is disabled until the target-specific service-flow proof exists.

Every registered operation has finite request/response/page byte limits and a
maximum page count. Chunked responses are counted while streaming; oversized
data is rejected before it can become an unbounded in-memory buffer. Automatic
decompression and ambient `Accept-Encoding` are disabled in the initial release;
any received `Content-Encoding` is rejected before JSON/XML parsing. A later
allowlisted decompression feature must enforce the same byte bound after
decompression.
Service-document and CSDL responses have their own finite byte limits and are
parsed with DTD/external-entity resolution disabled plus bounded XML depth,
character, and name counts.

Before production, the implementation must pass:

- concurrent five-product/two-profile fake-server isolation tests;
- per-migrated-product CI gates banning `Microsoft.Xrm*`,
  `Microsoft.CrmSdk*`, `Microsoft.PowerPlatform.Dataverse*`,
  `IOrganizationService`, `ICrmConnectionPool`, `ToolUtilityFactory`, and
  legacy CRM helper paths unless the exact file/use case is on the temporary
  legacy matrix with owner and removal deadline;
- profile reload/drain/disposal tests under traffic;
- a soak test with bounded memory/handle/socket/timer/queue growth;
- fault injection for 401, 429, 503, timeout, cancellation, metadata error,
  invalid config, secret rotation, admission-epoch change, host-slot expiry,
  fenced-slot quarantine, Gateway restart, and Embedded-host restart;
- Gateway-versus-Embedded product JSON/schema tests, including Visual Studio
  fake/local-Gateway profiles and rejection of production secret/organization
  use from development settings;
- warm-up/login-path tests proving no user/LINE/session/token data enters a
  pool, cache, queue, audit event, metric tag, or correlation ID;
- real non-production CE 8.2 and CE 9.1 smoke tests;
- direct-Web-API versus gateway performance baseline tests; and
- repository-wide no-SDK dependency scans after migration.

Audit/telemetry use redacted, bounded event payloads and a documented retention
period; raw CRM bodies, tokens, cookies, authorization headers and PII-rich URLs
are prohibited from audit storage.

Write retries use a CRM alternate key/upsert where possible. Otherwise a
durable, shared, bounded idempotency ledger is mandatory across Gateway and
Embedded runtime hosts; if it is unavailable, the host rejects the
ledger-dependent write before it reaches Dynamics rather than using sticky
sessions or replaying the command.

The ledger atomically creates-or-reads the typed tuple
`(workloadSubjectId, logicalProfileId, expectedOrganizationId, capabilityOperationId, idempotencyKey)`
serialized with the same versioned length-prefixed canonical encoding as other
cross-process keys.
It has a fixed bounded TTL and stores the immutable operation-definition
revision/template hash plus only a fingerprint hash and a minimal
redacted outcome/reference—never a raw body, token, credential, or unbounded
response. A write that cannot complete this atomic ledger step fails before an
outbound CRM request.

The ledger writes `Pending` before dispatch. A confirmed result becomes
`Completed`; any post-dispatch cancellation, process loss, or transport
uncertainty becomes `OutcomeUnknown` and is retained for the published retry and
reconciliation window. Equal duplicate calls return the existing completed,
in-progress, or outcome-unknown result; they never re-send an uncertain write.
Only a CRM alternate-key/upsert (or another proven CRM idempotency semantic)
permits automatic retry after uncertainty. The same idempotency identity under a
different operation revision is a typed conflict, never a rebind/re-execution.

Idempotency keys have a strict bounded URL-safe format. Per-workload/global
ledger entry and byte quotas reject new work before CRM dispatch, and a
versioned HMAC digest of the canonical typed envelope including the immutable
operation revision detects a mismatched reuse
of the same key without retaining the raw request body.

The HMAC key is a separate secret-provider reference from CRM credentials. Its
previous verification version remains through the whole ledger retention window
and is removed only after all hosts acknowledge the new version and no retained
record needs it. Missing verification material/KMS access makes ledger-dependent
writes fail closed before dispatch; neither key nor raw fingerprint input may
enter telemetry.

After each controlled runtime drain, disposal counters and weak-reference
sentinels must show that retired handlers, workers, timers, streams,
cancellation registrations, queues, leases, and strong runtime references are
gone by the declared deadline. Any cross-profile/session/token/credential/cache
signal, retained retired runtime, or unexplained sustained post-warm-up memory
or native-resource growth blocks release.

Telemetry uses an allowlisted/redacting adapter: URLs/query strings, headers,
bodies, and serialized exception objects are removed before export. Every
organization-operation response/error uses `Cache-Control: no-store, private`,
and no output/shared response cache applies to those routes.

Audit retention has a hard entry/byte quota and requires a durable atomic audit
intent before CRM dispatch. A ledger-dependent write creates `Pending` ledger
state and `Reserved` audit intent in one durable transaction, transitions the
intent to `Dispatching` before CRM traffic, and recovers every crash after that
boundary as a retained `OutcomeUnknown` audit/ledger record. Reservations are
released only by a durable terminal/recovery transition. A retention-job failure
alerts immediately; reservations throttle at high-water and fail closed at hard
quota rather than creating an unbounded in-memory queue or silently dropping
audit events. Non-audit telemetry uses a separate bounded queue with visible
drop metrics.

## Final dependency rule

~~~text
Gateway mode:  Product -> Gateway REST contract -> Gateway -> HttpClient/OData v4 -> Dynamics
Embedded mode: Product -> Embedded adapter -> HttpClient/OData v4 -> Dynamics
~~~

The final solution must contain no project reference to a DLL under
D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL, no CRM SDK package/type
dependency in production **or test**, and no CRM 2011 OrganizationData.svc
fallback.

## Evidence sources

- [Use the Dynamics 365 Customer Engagement Web API](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/use-microsoft-dynamics-365-web-api?view=op-9-1)
- [Dynamics 365 Customer Engagement Web API versions](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/webapi/web-api-versions?view=op-9-1)
- [Dynamics CRM 2016 Web API limitations (archived v8.x reference)](https://learn.microsoft.com/en-us/previous-versions/dynamicscrm-2016/developers-guide/mt628816(v=crm.8))
- [Authenticate to Dynamics 365 Customer Engagement with the Web API](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/webapi/authenticate-web-api?view=op-9-1)
- [Use connection strings in XRM tooling](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/xrm-tooling/use-connection-strings-xrm-tooling-connect?view=op-9-1)
- [Microsoft Dataverse Web API service documents](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/web-api-service-documents)
- [Guidelines for using HttpClient](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- [Do not use the OData v2 endpoint](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/best-practices/business-logic/do-not-use-odata-v2-endpoint)
