# Phase 4 Typed Dynamics Response Boundary Implementation Plan

> **For agentic workers:** implement this plan in test-first order. This is a bounded Phase 4 safety slice, not a consumer rollout.

**Goal:** Prevent raw Dynamics/OData JSON and continuation URLs from crossing the Gateway/ProductClient boundary while preserving the disabled Package 1 rollout state.

**Architecture:** `SpeechMessage.Dynamics.Abstractions` owns one closed response envelope and Package 1 wire records. The Web API connector reads each bounded CRM page, validates any continuation before the next credential-bearing request, projects only the registered fields into that envelope, and disposes every page resource in the same request scope. Gateway and ProductClient serialize/deserialize only this closed shape; the ProductClient maps it to existing product DTOs without parsing OData JSON.

**Hard invariants:** no `JsonElement`, `object`, OData annotation, CRM hostname, API root, continuation URL, credential, token, session, or upstream extension data may enter `OperationExecutionResult.Data`. Paging is bounded by registered per-operation page, row, and byte limits; malformed, cross-root, wrong-version, cyclic, or over-limit continuation returns a controlled failure before an additional credential-bearing request. `Package01FeeReadsEnabled` remains `false`.

---

### Task 1: Closed response contracts and registry policy

**Files:**

- Create: `SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs`
- Modify: `SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionResult.cs`
- Modify: `SpeechMessage.Dynamics.Abstractions/Operations/OperationDefinition.cs`
- Modify: `SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs`
- Modify: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json`
- Modify: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json`
- Test: `SpeechMessage.Dynamics.Tests/Package01OperationRegistryTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/OperationRegistryAgreementTests.cs`

- [ ] Write failing tests that require each registered operation to declare a closed response kind and finite page/byte policy, and require Package 1 fee/stor records to serialize without OData field names.
- [ ] Verify the tests fail because the current registry exposes only generic `object?` response data and no page policy.
- [ ] Add a closed, concrete `OperationResponseData` union with only `operationId`, `ceVersion`, an enum discriminator, `whoAmI`, `feeRecords`, or `storLessonRecords`; use JSON null omission so only the selected branch appears.
- [ ] Add immutable Package 1 wire records that mirror the existing public product DTO fields, including nullable compatibility fields and zero-default fee amount.
- [ ] Change `OperationExecutionResult.Data` and `Success` to this closed type; add response kind and finite per-page, cumulative-byte, and result-row limits to `OperationDefinition` and include all of them in the template hash.
- [ ] Register `WhoAmI`, fee rows, and stor rows explicitly. Mark the currently unmapped metadata operation as unsupported at the product-response boundary so it fails closed rather than exposing raw metadata.
- [ ] Update only the nine registered matrix rows and agreement test fields to pin the response kind and policy alongside the revised hash.

### Task 2: Connector-owned projection and server-side paging

**Files:**

- Modify: `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
- Test: `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`

- [ ] Write failing fake-transport tests for: typed fee response has no CRM/OData fields; a relative continuation and an allowed same-root absolute continuation aggregate rows; cross-origin, wrong-base-path, wrong-version, malformed, cycle, page-count, per-page, and cumulative-byte violations fail before a second unauthorized target request.
- [ ] Verify every new test fails against the current first-page-only generic payload behavior.
- [ ] Replace recursive annotation stripping with an operation-owned projector. It accepts only the exact registered CRM fields plus the required formatted-value annotation and harmless known metadata annotations; it rejects unknown raw fields/types rather than retaining them.
- [ ] Narrow the request `Prefer` header to the one formatted-value annotation the registered projection requires.
- [ ] Make one request-scope paging loop own the visited-link set and aggregate record list. Validate a candidate continuation under `ApprovedWebApiRoot` before applying authorization or creating the next request. Enforce policy limits before returning a partial response.
- [ ] Preserve the existing deterministic ownership of `HttpRequestMessage`, `HttpResponseMessage`, content stream, timeout CTS, and zeroed `ArrayPool` buffer on success, retry, parse failure, cancellation, and paging rejection.

### Task 3: Gateway/ProductClient closed-contract consumption

**Files:**

- Modify: `SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs`
- Modify: `SpeechMessage.Dynamics.ProductClient/FeeReads/Package01FeeReadClient.cs`
- Test: `SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs`
- Test: `SpeechMessage.Dynamics.Tests/Package01FeeReadClientTests.cs`

- [ ] Write failing tests that deserialize Gateway data as the shared closed contract, reject unknown response members, and show Package 1 product reads map from shared records without `JsonElement`/OData parsing.
- [ ] Verify the tests fail because the current Gateway executor converts `JsonElement` back to `object?` and Package 1 parses `data.value` itself.
- [ ] Deserialize only `OperationResponseData` in Gateway mode, use `JsonUnmappedMemberHandling.Disallow`, and return a sanitized upstream failure for a malformed/unknown response contract.
- [ ] Replace `Package01FeeReadClient` raw JSON coercion helpers with one-to-one mapping from the closed shared records. Reject a mismatched response kind/operation ID before product DTO creation.
- [ ] Keep the existing product DTO public surface and its null/default behavior unchanged.

### Task 4: Verification and records

**Files:**

- Modify: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-durable-sql-live-verification-2026-07-31.md` (append only, only after fresh evidence)

- [ ] Run focused response-boundary tests, the complete Dynamics Debug/Release test suites, and the Release solution build.
- [ ] Inspect the changed-file diff for residual `object?`/`JsonElement` response paths, raw `@odata`/CRM host strings, Package 1 enablement changes, and unchecked request resource ownership.
- [ ] Run UTF-8-without-BOM, CRLF, final-CRLF, `git diff --check`, and changed-line sensitive literal checks.
- [ ] Record only actual test/build evidence and remaining external blockers; do not claim a LocalDB or live CE result that was not freshly obtained.
