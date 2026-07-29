# Phase 4 Local/Central Gateway boundary verification

## Scope

This milestone makes Central Gateway and Local Gateway safe deployments of the
same product-facing `ExecutionMode=Gateway` contract. It does not enable product
traffic, create the CE 8.2 worker, remove Data8, expand Embedded, or change
`Package01FeeReadsEnabled=false`.

## Implemented boundary

- Added a dedicated `GatewayProductDynamicsOptionsValidator` registered through
  `IValidateOptions<ProductDynamicsOptions>` and `ValidateOnStart()`.
- Accepted both the Central endpoint example
  `https://dynamics-gateway.internal/` and the current Local endpoint
  `https://localhost:7244/` without adding Central/Local enum values.
- Rejected HTTP, URI user-info/query/fragment, raw `/api/data/`,
  `/XRMServices/`, `Organization.svc`, unsafe API prefixes, unsafe or oversized
  profile aliases, an inactive Embedded branch, and response limits outside
  1 KiB through 8 MiB.
- Added `Gateway.MaxResponseBytes`, defaulting to 2 MiB.
- Pinned every request to the deployment-configured profile alias. A differing
  request alias is rejected before any HTTP send.
- Changed the ProductClient to `ResponseHeadersRead` and one bounded reader for
  both declared `Content-Length` and chunked responses.
- Limited every rented read buffer to at most 16 KiB, disposed response/stream
  objects deterministically, and zeroed both rented and temporary payload
  buffers before release.
- Preserved caller cancellation as `OperationCanceledException`; other
  transport/read failures return sanitized errors and log only exception type.
- Upgraded only `System.Security.Cryptography.Xml` from `10.0.9` to `10.0.10`
  in the temporary Data8 project.

## TDD evidence

### Startup validator

Before implementation:

```text
ProductModeOptionsTests
Failed 12, Passed 4
```

The failures were the expected unsafe endpoint, API-prefix, and inactive
Embedded cases. Additional red tests then covered unsafe/oversized aliases and
response-limit bounds.

After implementation:

```text
ProductModeOptionsTests
Failed 0, Passed 26
```

### ProductClient profile and response boundary

Before implementation, the following tests failed for their expected reasons:

- request alias override reached HTTP instead of failing before send;
- declared oversized content was read by the default buffered completion path;
- chunked oversized content produced only a JSON parse error rather than a byte-limit error;
- caller cancellation was converted to a failure result.

After implementation:

```text
GatewayProductClientTests
Failed 0, Passed 7
```

The tests also prove that a chunked oversized response stream is disposed and a
declared oversized body is rejected before a body read is attempted.

## Dependency-security evidence

Before the package change, NuGet reported five High advisories for:

```text
System.Security.Cryptography.Xml 10.0.9
```

After changing only that package to `10.0.10`:

```text
No vulnerable package is reported for PowerPlatform.Dataverse.Client.
```

This patch does not change the architectural status of Data8. It remains a
temporary CE 8.2 compatibility dependency that must be process-isolated and
removed after a proven Web API v8.2 or official net48 `CrmServiceClient` worker
replacement passes the documented gates.

## Final local verification

```text
dotnet test SpeechMessage.Dynamics.Tests --configuration Release --no-restore
Passed 125, Failed 0, Skipped 0

dotnet build SpeechMessageProducts.sln --configuration Release --no-restore
Build succeeded, 0 warnings, 0 errors

dotnet list PowerPlatform.Dataverse.Client package --vulnerable --include-transitive
No vulnerable package reported

git diff --check
Passed
```

Additional checks passed for nine touched text files:

- UTF-8 without BOM;
- CRLF line endings;
- no added password, bearer token, client secret, private key, or refresh token;
- no static/shared ProductClient session, token, credential, `HttpClient`,
  `AsyncLocal`, `ThreadLocal`, or default authorization-header state.

## CCG review

Run:

```text
20260729-135309-dynamics-local-central-boundary-implementation-reviewer
```

- Gemini completed and recommended PASS with no Critical finding.
- Claude was blocked by provider session quota and produced no output.
- Runner state: `degradedFallback=true`, `fallbackAccepted=true`,
  `quotaBlocked=true`.

This is a single-model degraded review, not a completed dual-model review.
Gemini's one Warning was that Data8/WS-Trust remains legacy technical debt. The
finding is valid and already enforced by the SPEC: Data8 is temporary, cannot
become the permanent Gateway pool, and remains subject to worker isolation and
Phase 6 removal gates. No new unresolved Warning was identified in the boundary
implementation itself.

## Remaining gates

This milestone is complete, but the overall Dynamics objective is not. The next
required work remains:

1. immutable `crm82`/`crm91` profile generations, routing, replace-and-drain,
   shared organization admission, and multi-profile soak tests;
2. a bounded recyclable CE 8.2 Legacy Worker and later official replacement;
3. authenticated WinRM administration and DC/D365 VM role/configuration proof;
4. live CE 8.2 and CE 9.1 smoke tests without recording secrets;
5. ChurchReport Local Gateway startup, feature-flagged migration, rollback, and
   browser end-to-end verification;
6. Phase 4 resource/performance soak, Phase 5 product migration, and Phase 6
   final Data8/SDK removal.
