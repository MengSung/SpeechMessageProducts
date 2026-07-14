# F02 Security Analysis

Status: COMPLETE
Module: F02 - Dataverse Connection Foundation
Mode: DIAGNOSIS_ONLY

## Method And Trust Boundaries

The review traced organization URL input, remote WSDL/STS metadata, username
credentials, AD/WS-Trust token exchange, WCF channel configuration, CallerId
impersonation headers, XML/SOAP parsing, transport limits, and credential-bearing
object lifetime.

Trust boundaries:

1. Host configuration supplies the initial organization URL and credentials.
2. The configured HTTPS organization endpoint supplies WSDL policy and imports.
3. Federation metadata supplies issuer metadata and STS endpoints.
4. The selected CRM/STS endpoint supplies SOAP/XML responses.
5. F02 emits authenticated `IOrganizationService` calls for pool consumers.

No direct credential, token, or cross-user disclosure was confirmed.

## Confirmed Finding: F02-SEC-001

### Remote Metadata And Authentication Responses Lack Bounded Resource Policy

Evidence:

- `ClaimsBasedAuthClient.cs:184-190` sets the federated WCF message size,
  buffer pool, string, array, bytes-per-read, and name-table quotas to
  `Int32.MaxValue`.
- `Wsdl.cs:64-79` downloads and recursively deserializes every unique import
  without response-size, import-count, depth, or elapsed-time limits.
- `ADAuthHelpers/BaseAuthRequest.cs:49-85` sends WS-Trust authentication
  requests without applying `ADAuthClient.Timeout`.
- `ADAuthClient.cs:160-185` continues token exchanges until a final collection
  appears, with no negotiation-round cap.
- `ADAuthClient.cs:292-304` streams organization responses into XML/WCF
  deserialization; the operation request timeout at line 273 limits elapsed
  request time but not total response allocation.
- `ADAuthClient.cs:168-183` can repeat individually finite requests without an
  overall handshake deadline.

Source/control/sink flow:

1. A configured CRM/STS endpoint, compromised endpoint, or misconfigured
   upstream returns a large or indefinitely continuing metadata/auth response.
2. F02 accepts the response under effectively unbounded federated quotas or
   recursively follows metadata/auth exchanges.
3. XML/WCF deserialization, memory allocation, network I/O, and authentication
   work occur inside client construction or an organization request.
4. Pool initialization or replacement can repeat the cost for several clients.

Guards and counter-evidence:

- `OnPremiseClient.cs:125-126` requires HTTPS for the initial organization URL.
- AD organization operations set a per-request timeout at
  `ADAuthClient.cs:270-274`.
- `WsdlLoader` uses a per-call `HashSet` at `Wsdl.cs:55-62`, preventing an exact
  import URL cycle within one load.
- `BaseAuthRequest` accepts only expected RSTR/RSTRCollection roots at
  `BaseAuthRequest.cs:77-85`.
- These guards do not bound response bytes, unique import fan-out, handshake
  rounds, or total construction budget.

Impact:

- A reachable or compromised trusted upstream can cause high memory use,
  prolonged thread blocking, repeated XML work, or connection-pool starvation.
- This is an availability and resource-exhaustion issue, not a demonstrated
  credential disclosure or arbitrary-code execution issue.

Recommended boundary:

- Introduce one F02-owned transport/resource policy with explicit response
  bytes, XML quotas, WSDL import count/depth, per-request timeout, overall
  discovery/authentication deadline, and negotiation-round limit.
- Preserve protocol-required large-response support through documented,
  finite configuration rather than `Int32.MaxValue`.

## Confirmed Finding: F02-SEC-002

### Remote Metadata Can Redirect Discovery Outside The Configured Origin

Evidence:

- The initial URL has an HTTPS-only guard at `OnPremiseClient.cs:123-126`.
- Organization WSDL is loaded at `OnPremiseClient.cs:128-139`.
- `Wsdl.cs:73-79` recursively passes each remote `import.Location` directly to
  `WebRequest.CreateHttp` without same-origin, scheme, port, address-range, or
  allowlist validation.
- Federation takes the issuer metadata address from remote WSDL at
  `OnPremiseClient.cs:171-182` and loads it directly.
- The discovered STS service address is selected at
  `OnPremiseClient.cs:192-224`.

Source/control/sink flow:

1. An administrator configures a legitimate HTTPS CRM endpoint.
2. That endpoint's metadata, or a compromised/malicious endpoint trusted by
   the deployment, returns an HTTP(S) import or issuer metadata URL.
3. The server-side F02 process performs an outbound request to that location.
4. The fetched metadata can influence later STS endpoint selection.

Exploit conditions:

- The attacker must control metadata returned by the configured endpoint,
  compromise its TLS/DNS/server path, or persuade an operator to configure a
  hostile CRM endpoint.
- No evidence shows that WSDL GETs forward the supplied username/password.
- `WebRequest.CreateHttp` narrows the direct fetch mechanism to HTTP(S).
- Federated username credentials are sent through
  `SecurityMode.TransportWithMessageCredential` at
  `ClaimsBasedAuthClient.cs:165-176`, which is counter-evidence against a
  plaintext credential-leak claim.

Impact:

- The application can be induced to make server-side HTTP(S) requests outside
  the configured CRM origin and to trust externally redirected STS metadata.
- The retained claim is SSRF/trust-boundary expansion under the stated
  preconditions. It does not claim immediate credential exfiltration.

Recommended boundary:

- Resolve imports against the source document URI.
- Require HTTPS and an explicit origin/host/port policy for WSDL, issuer
  metadata, and STS endpoints.
- Reject loopback, link-local, private, or cross-origin destinations unless
  specifically approved for the on-prem deployment.
- Return a validated immutable endpoint profile before credentials are applied.

## Credential And Session Lifetime Observation

`ADAuthClient.cs:38-45` retains username/password and token state for refresh.
The federated factory holds configured credentials and a live channel. Because
the public wrapper lacks a disposal contract, these objects can outlive the
consumer's attempted cleanup. This is retained primarily as
`F02-PERF-001`; no cross-user read path or credential log sink was found.

## Rejected Or Narrowed Security Candidates

### SHA-1/HMAC-SHA1 Is Automatically A Critical Vulnerability

Rejected. `ADAuthHelpers/Authenticator.cs:30-35,73-95` uses SHA-1/P_SHA1 in the
legacy WS-Trust authenticator protocol. The code validates the returned
authenticator. Algorithm age alone does not establish a practical bypass in
this protocol context.

### CallerId Causes Current Cross-User Impersonation

Rejected as confirmed. `OnPremiseClient.cs:260-291` and
`ADAuthClient.cs:103-106,267-268` expose mutable CallerId state, but no current
consumer assignment was found. Pool leasing appears exclusive, and no caller
of `ParallelRetrieveAsync` was found. The future transport lease must still
document that one client is not a concurrent multi-identity object.

### NSspi Is The Current Authentication Path And Leaks Native Handles

Rejected. The net10 target selects `NegotiateAuthentication` under
`NET7_0_OR_GREATER` at `ADAuthClient.cs:116-154`. NSspi context and credential
types generally implement disposal, for example
`NSspi/Contexts/Context.cs:109-131` and
`NSspi/Credentials/Credential.cs:184-207`.

### Exception Messages Expose Passwords Or Tokens

Rejected. Reviewed construction errors include URL and issuer endpoint but not
the supplied password, proof token, or security context token.

## Cross-Module Handoffs

- X04A owns whether configured CRM URLs and credentials come from an approved
  secret/configuration source.
- X01 owns pool sizing, singleton registration, and host shutdown behavior.
- F03A/F03Q consumers must obey the future lease/concurrency contract.
- F02 owns endpoint validation, authentication resource limits, transport
  security, and client/session disposal.
