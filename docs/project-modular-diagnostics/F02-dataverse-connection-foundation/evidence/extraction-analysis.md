# F02 Extraction Analysis

Status: COMPLETE
Module: F02 - Dataverse Connection Foundation
Mode: DIAGNOSIS_ONLY

## Extraction Lens

F02 extraction must preserve one provider direction:

`F02 connection/authentication/transport => F03A/F03Q/X02C consumers`

It must not absorb ChurchReport query semantics. A useful boundary separates
remote metadata, authentication session, transport lifetime, and SDK request
execution so each can be tested and optimized independently.

## Confirmed Finding: F02-EXT-001

### Construction Collapses Discovery, Authentication, Transport, And Lifetime

Owning files and responsibilities:

- `OnPremiseClient.cs:97-168`: public construction, HTTPS guard, metadata
  discovery, authentication-type selection, and connection creation.
- `OnPremiseClient.cs:171-258`: STS discovery, credential application, WCF
  channel creation, and AD client creation.
- `Wsdl.cs:51-82`: remote metadata transport, recursion, and deserialization.
- `ClaimsBasedAuthClient.cs:46-199`: federated binding/factory/channel setup.
- `ADAuthClient.cs:38-217`: credentials, token state, authentication handshake,
  and refresh.
- `ADAuthClient.cs:255-368` and `OnPremiseClient.cs:289-310` plus remaining
  methods: SDK request execution/wrapping.
- `PowerPlatform.Dataverse.Client.csproj:3-63`: target/package/project
  lifecycle.

Current contract:

- Input: URL, credentials, `IOrganizationService` requests, timeout, CallerId.
- Output: a concrete wrapper erased to `IOrganizationService`.
- Hidden dependencies: synchronous `WebRequest`, XML serializers, clock,
  remote metadata graph, WCF factory/channel, AD negotiation, token cache, and
  cleanup rules.

Consumer evidence:

- `CrmConnectionService.cs:430-435` directly constructs the concrete type and
  returns only `IOrganizationService`.
- `CrmConnectionPool.cs:293-315` depends on that construction path.
- The map names F03A, F03Q, and X02C as consumers.
- No direct F02 tests or injected metadata/auth/transport fakes exist.

Why this is a real extraction issue:

- The constructor performs network I/O and chooses protocol, so consumers
  cannot separately cache metadata, test endpoint policy, control
  authentication deadlines, or own disposal.
- The erased result explains both confirmed repeated discovery and missing
  deterministic cleanup.
- A test must currently emulate a complete CRM/STS SOAP environment rather
  than target one responsibility.

Recommended contracts:

1. `IOrganizationMetadataResolver`
   - input: normalized organization URI, SDK major version, discovery policy;
   - output: immutable validated authentication profile and endpoint set;
   - owns bounded metadata fetch/parse and cache semantics.
2. `IAuthenticationSessionFactory`
   - input: validated profile, credential source, deadline;
   - output: disposable AD token session or federated channel credentials;
   - owns negotiation limits and secret lifetime.
3. `IOrganizationTransportFactory`
   - input: validated profile and authentication session;
   - output: disposable `IOrganizationServiceLease`;
   - owns channel create/close/abort, timeout, fault classification, and
     optional retry hooks.
4. Compatibility adapter
   - exposes `IOrganizationService` operations and CallerId scope;
   - does not hide lease ownership from the pool.

Dependency direction:

- Resolver/authentication/transport are F02 internals behind F02-owned public
  contracts.
- F03A consumes the lease and continues to own CRUD/query shape, batching, and
  entity semantics.
- X01 owns DI and pool policy; X02C observes transport timing without owning
  connection behavior.

Test seams:

- Metadata fixtures for import/origin/size/depth policy.
- Fake clock and token expiry.
- Finite AD handshake transcript fixtures.
- Fake WCF communication states for close versus abort.
- Consumer contract fixture proving F03A can use a lease as
  `IOrganizationService` without losing deterministic disposal.

Rollback boundary:

- Add interfaces/adapters first while retaining the current constructor.
- Move discovery, authentication, then transport ownership in separate F02
  commits.
- Migrate F03A/F03Q/X01 consumers only through owner-scoped follow-up tasks.

Loop leverage:

- One metadata seam addresses repeated discovery and endpoint validation.
- One lease seam addresses disposal, timeout, concurrency, and observability.
- Provider fixtures unlock the gate required before F02 optimization can be
  claimed complete.

## Clean Query Boundary

F02 should not introduce a ChurchReport query abstraction. Its reusable
"query seam" is the transport-level `IOrganizationServiceLease` or request
executor that accepts SDK requests and returns SDK responses. F03A remains the
owner of QueryExpression/FetchXML construction, column selection, pagination,
batching, and N+1 elimination.

## NSspi Lifecycle Candidate

The main net10 project compiles bundled NSspi source through SDK default globs,
while `ADAuthClient.cs:116-154` selects `NegotiateAuthentication`. A separate
`NSspi.csproj` targets old frameworks, is excluded from the solution, generates
packages, signs with a committed key, and references a missing license file.

Recommended future decision:

- either exclude/archive the standalone project and remove unreachable source
  after compatibility confirmation;
- or make NSspi an explicitly supported, independently gated compatibility
  package.

This is not a retained confirmed issue because no current runtime path,
consumer, build failure, or material cost was established under the strict
no-build diagnosis.

## Rejected Extraction Candidates

### Move ToolUtility Queries Into F02

Rejected by the map and dependency direction. It would combine connection
foundation with F03A CRM operations and ChurchReport semantics.

### Extract ClaimsBasedAuthClient Without A Lifecycle Contract

Rejected. Moving the class alone would preserve hidden factory/channel
ownership and would not solve the confirmed issues.

### Treat The Connection Pool As F02-Owned

Rejected. Pool sizing, lease scheduling, health cadence, and host singleton
registration are F03A/X01 consumer policy. F02 should expose a correct lease,
not absorb the host pool.

### Create A Global Credential/Token Cache

Rejected. Metadata can be shared under bounded policy; live credentials,
tokens, CallerId, and channels require explicit per-connection/session
lifetime and identity isolation.

## Cross-Module Handoffs

1. F03A: consume the F02 lease and retain query/CRUD ownership.
2. F03Q: migrate mixed facade connection construction after the F02 adapter is
   available.
3. X01: bind factory/lease lifetime and pool shutdown in a separate host task.
4. X02C: instrument the transport seam without coupling profiling state into
   F02.
5. F01A: project/solution lifecycle only if the NSspi packaging decision
   changes enrollment.
