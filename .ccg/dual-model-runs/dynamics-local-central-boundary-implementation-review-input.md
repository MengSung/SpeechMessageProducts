# Dynamics Local/Central Gateway boundary implementation review

Review the current implementation milestone against:

- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `docs/superpowers/plans/2026-07-29-dynamics-local-central-gateway-boundary.md`

Inspect these changed or newly created files directly (two are untracked and therefore do not appear in a normal `git diff`):

- `SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs`
- `SpeechMessage.Dynamics.ProductClient/Configuration/GatewayProductClientLimits.cs`
- `SpeechMessage.Dynamics.ProductClient/Configuration/GatewayProductDynamicsOptionsValidator.cs`
- `SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs`
- `SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs`
- `SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs`
- `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`

Focus on Critical/Warning findings only:

1. Central and Local must remain the same `ExecutionMode=Gateway` contract selected by endpoint.
2. Startup validation must fail closed for HTTP, URI user-info/query/fragment, raw CRM Web API/SOAP endpoints, invalid API prefix, unsafe/oversized profile alias, inactive Embedded branch, and response bounds outside 1 KiB through 8 MiB.
3. A request cannot override the deployment-configured ProfileAlias and no HTTP request is sent on mismatch.
4. `HttpClient.SendAsync` must use `ResponseHeadersRead`; Content-Length and chunked bodies must share one hard byte limit.
5. Every response, content stream, rented array, temporary payload buffer, request, and cancellation path must have deterministic bounded ownership and cleanup. Check for memory, stream, socket, cancellation, or session leakage.
6. Caller cancellation must remain an `OperationCanceledException`; other transport/read failures must be sanitized and must not log raw URLs, bodies, credentials, tokens, sessions, or exception objects.
7. The bounded reader must have no off-by-one, overflow, unbounded allocation, infinite-loop, use-after-clear, or invalid disposal behavior.
8. `System.Security.Cryptography.Xml` changes only from vulnerable `10.0.9` to patched `10.0.10`; do not recommend keeping Data8 permanently.
9. Tests must actually fail on the old implementation and prove the relevant behavior rather than merely restating implementation details.

Local evidence already obtained:

- Product options RED: 12 failures / 4 passes before validator.
- Expanded ProductModeOptionsTests GREEN: 26/26.
- GatewayProductClientTests RED: profile override, declared overage, chunked overage, and caller cancellation failed before implementation.
- GatewayProductClientTests GREEN: 7/7.
- Complete Dynamics suite after implementation: 125/125.
- NuGet vulnerable baseline: five High advisories for `System.Security.Cryptography.Xml 10.0.9`.
- Post-upgrade audit: no vulnerable package reported for the Data8 project.
- Solution Release build before final refactor: 0 warnings, 0 errors; full build will be rerun after review fixes.

Output a concise report grouped by Critical / Warning / Info. Cite exact files and lines. Do not modify files.
