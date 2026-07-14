# F02 Performance Analysis

Status: COMPLETE
Module: F02 - Dataverse Connection Foundation
Mode: DIAGNOSIS_ONLY

## Cost Model

F02 cost consists of metadata network I/O, XML deserialization, WS-Trust/AD
handshakes, WCF factory/channel creation, token refresh, synchronous SOAP
execution, and lifetime cleanup. Current consumers amortize some cost through
a pool, but pool construction and replacement also multiply F02 construction
cost.

## Confirmed Finding: F02-PERF-001

### The Public Client Cannot Release Its Channel And Authentication Resources

Owning evidence:

- `OnPremiseClient.cs:33` implements only `IOrganizationService`.
- Its `_service` field at line 67 can hold a WCF channel or `ADAuthClient`.
- The federated path creates `ClaimsBasedAuthClient`, lazily creates a
  `ChannelFactory`, configures credentials, creates a channel, and returns only
  the channel at `OnPremiseClient.cs:211-229`.
- `ClaimsBasedAuthClient.cs:46-64,108-145` owns the factory but has no
  `Dispose`, `Close`, or successful-path `Abort`.
- The AD path creates `NegotiateAuthentication` at
  `ADAuthClient.cs:124-130` without disposing it after authentication.
- `OnPremiseClient.cs:38-64` disposes only per-call
  `OperationContextScope`; it is not an outer client-lifetime cleanup path.

Consumer reachability:

- `CrmConnectionService.cs:430-435` returns the concrete client as
  `IOrganizationService`.
- `CrmConnectionPool.cs:406-419` attempts
  `(connection.Service as IDisposable)?.Dispose()`.
- Because `OnPremiseClient` is not `IDisposable`, that cleanup is a no-op.
- The pool is registered singleton with default minimum 3 and maximum 20 at
  `Startup.cs:297-349`.

Lifetime flow:

1. Pool or facade constructs an F02 client.
2. F02 creates metadata/authentication objects and a live AD client or WCF
   channel/factory.
3. Consumer retains only `IOrganizationService`.
4. Idle cleanup, pool disposal, or ToolUtility disposal attempts an
   `IDisposable` cast.
5. The wrapper does not satisfy the cast, so F02 has no deterministic close,
   abort, or authentication-context cleanup.

Impact:

- WCF channels/factories, sockets, authentication contexts, token material,
  and related buffers rely on indirect/runtime cleanup rather than an explicit
  lifecycle.
- Failed/replaced connections and token refreshes can accumulate resource
  pressure over a long-lived singleton pool.
- Exact handle/socket growth requires future runtime measurement, but the
  missing deterministic cleanup path is statically confirmed.

Recommended action:

- Make the F02 connection result an explicit disposable lease.
- Close healthy WCF channels/factories and abort faulted ones.
- Dispose `NegotiateAuthentication`, cryptographic digest state, and other
  owned resources on success and failure.
- Keep compatibility through an `IOrganizationService` adapter while exposing
  lifecycle to pool owners.

## Confirmed Finding: F02-PERF-002

### Every Client Construction Repeats Synchronous Metadata Discovery

Evidence:

- Every `OnPremiseClient` constructor reaches
  `WsdlLoader.Load(...).ToList()` at `OnPremiseClient.cs:123-139`.
- Federation performs another STS metadata load and full materialization at
  `OnPremiseClient.cs:171-209`.
- `WsdlLoader.Load` creates a new local `HashSet` per invocation at
  `Wsdl.cs:53-62`; there is no shared cache across clients.
- Each document is synchronously fetched and deserialized at
  `Wsdl.cs:64-79`.
- The pool eagerly constructs its minimum clients at
  `CrmConnectionPool.cs:274-287`, creates each through F02 at lines 293-315,
  and can grow to the configured maximum.
- Host defaults are min 3/max 20 at `Startup.cs:326-348`.

Call/cost flow:

1. First pool resolution constructs at least three clients.
2. Each client independently downloads and parses the organization WSDL import
   graph.
3. Federation also independently downloads and parses the STS metadata graph.
4. Pool growth, unhealthy replacement, or another facade repeats the same
   discovery for the same endpoint.

Guards and counter-evidence:

- The pool reuses healthy clients and throttles health checks, so this is not
  claimed as per-request metadata discovery.
- `WsdlLoader` suppresses duplicate URLs only inside one load operation.
- No cross-client metadata cache, immutable discovery profile, or
  single-flight construction guard exists in F02.

Impact:

- Deterministic startup/replacement latency, repeated network I/O, repeated XML
  allocations, and avoidable load on CRM/STS metadata endpoints.
- Exact elapsed-time savings remain a future measurement; the repeated call
  count and missing shared cache are statically established.

Recommended action:

- Extract a validated immutable metadata/authentication profile keyed by
  normalized organization URL and SDK major version.
- Use bounded TTL, single-flight population, failure eviction, and no
  credential material in the cache.
- Keep live channels and tokens per connection; cache only safe discovery
  metadata.

## Related Availability Cost

`F02-SEC-001` records unbounded WCF/XML/handshake controls. Its CPU, memory, and
blocking-I/O consequences are not duplicated as another performance issue.

## Rejected Or Narrowed Performance Candidates

### Every CRM Request Rebuilds The Client

Rejected. The host uses a connection pool and returns leased clients; normal
requests do not necessarily construct a new F02 client.

### Current Business Queries Are N+1 Because Of F02

Rejected by ownership. F02 transports `IOrganizationService` requests but does
not choose ChurchReport query shape. N+1/query optimization belongs to F03A or
the relevant business module.

### Mutable CallerId Has A Confirmed Lock-Contention Or Race Cost

Rejected. No current assignment or caller of the parallel extension was found.
The future lease contract should prohibit concurrent identity mutation, but no
current performance loss is proven.

### Bundled NSspi Is A Material Runtime Hot Path

Rejected. Net10 uses `NegotiateAuthentication`. The SDK default compile glob
still includes NSspi source, but no measured or dominant compile/package cost
was established. Preserve it as a project-lifecycle cleanup candidate.

### Missing Retry Causes Confirmed Excessive Load

Rejected as a standalone performance issue. F02 has no explicit retry policy,
but absence alone does not establish repeated calls or waste. A future
transport seam should classify transient failures and make retry policy
explicit rather than hiding retries inside the client.
