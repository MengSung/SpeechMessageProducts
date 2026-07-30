# Gateway HTTP Request Body、Canonical Dispatch 與 Queue Retention 盤點

研究日期：2026-07-29  
範圍：Phase 4 下一個 hard HTTP body／canonical dispatch／queue lifecycle TDD slice  
限制：僅研究，不修改實作、不 commit；目前 AuthN/AuthZ 代理正在修改 `Program.cs`、`Security/*`、`appsettings.Development.json`、`GatewayKestrelNegotiateTests.cs`、`GatewayWorkloadBoundaryTests.cs`。

## Files Found

- `SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs:56-125,209-279`：產品端建立 Gateway JSON body；目前淺拷貝 Parameters，未限制 outbound request bytes。
- `SpeechMessage.Dynamics.Gateway/Program.cs:25-29,166-202,334-340`：Minimal API JSON binding、principal authorization、HTTP DTO 到 `OperationExecutionRequest` 的邊界。
- `SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionRequest.cs:14-45`：canonical executor DTO；`Parameters` 僅是 `IReadOnlyDictionary` 介面，沒有 defensive typed copy、size 或 disposal contract。
- `SpeechMessage.Dynamics.Abstractions/Operations/OperationDefinition.cs:16-60`：registry parameter name/type/required/encoding contract。
- `SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs:41-175`：目前 operation 每項只宣告 0 至 3 個 scalar parameter，但此小數量沒有在 request boundary 形成 hard count/type gate。
- `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs:45-161`：目前建立 `DispatchEnvelope`、粗估 envelope bytes、等待 admission，之後再使用原始 `request.Parameters`。
- `SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs:16-27`：目前 queue metadata envelope；只含字串、deadline、estimated byte count、correlation ID，不含參數本文。
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs:155-274,875-955`：先等 admission、後取當下 Active Runtime；combined lease 先釋放 runtime lease，再釋放 permit，兩個 cleanup 都會嘗試。
- `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs:176-355,364-390,484-545,571-815`：bounded semaphore queue、linked cancellation、workload/counter reservation 與 shutdown drain。
- `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionOptions.cs:18-116`、`OrganizationAdmissionPlan.cs:43-199`：queue、per-workload、dispatch bytes、timeout 與 lifecycle 設定。
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs:71-131,175-217,458-503,520-697`：required/type conversion 與 template binding 現在發生在 admission 之後；既有 response reader 可作為 Content-Length／chunked／pooled-buffer-zeroing pattern。
- `SpeechMessage.Dynamics.WebApi/Runtime/FetchXmlValueEncoder.cs:39-140`：以 `Convert.ToString(object)` 接受值，沒有先保證值符合 registry declared type。
- `SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs:44-298`：已有 queue capacity、workload cap、permit release 與 gauge baseline pattern。
- `SpeechMessage.Dynamics.Tests/OrganizationAdmissionLeaseLifecycleTests.cs:49-70`：已有 dispose 等待 active permits、fenced slot release pattern。
- `SpeechMessage.Dynamics.Tests/ControlledOperationExecutorTests.cs:20-107`：已有 unknown operation/parameter、lease-loss cancellation、permit disposal pattern。
- `SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs:106-170,209-270` 與 `DynamicsWebApiClientTests.cs`：已有 declared response oversize、chunked response oversize、stream disposal、byte buffer clear pattern；這些只保護 response，不保護 Gateway inbound body。

## Dependencies / Exact Data Flow

```text
Product caller
  OperationExecutionRequest
    ProfileAlias / CapabilityOperationId / WorkloadSubjectId / IdempotencyKey
    IReadOnlyDictionary<string, object?> Parameters
        |
        | GatewayDynamicsOperationExecutor.ExecuteAsync
        | - ToDictionary() shallow copy
        | - GatewayOperationHttpBody + JsonContent retain copied dictionary/value graph
        v
HTTP POST /v1/organizations/{alias}/operations/{operationId}
        |
        | ASP.NET Core Minimal API JSON binding
        | OperationHttpRequest.Parameters : Dictionary<string, object?>
        | object values normally materialize as JsonElement
        v
Program endpoint
  httpContext.User -> IGatewayOperationAuthorizer.Authorize
  authorization result -> bounded server-owned workload/alias/operation strings
  OperationExecutionRequest.Parameters = body.Parameters (same dictionary reference)
        |
        v
ProfileRoutedOperationExecutor / DynamicsProfileRuntimeManager.ExecuteAsync
        |
        v
ControlledOperationExecutor.ExecuteAsync
  registry lookup
  unknown-name scan
  DispatchEnvelope
  EstimatedEnvelopeBytes = UTF-16 heuristic
        |
        v
IProfileExecutionLeaseProvider.AcquireAsync(envelope)
  DynamicsProfileRuntimeManager resolves only admission manager + immutable plan
        |
        v
OrganizationAdmissionManager.AcquireAsync
  _totalAdmission semaphore -> workload count -> _queued
  linked caller/admission-stop/lease-lost CTS
  _inFlight.WaitAsync(timeout)
        |
        | dequeue / permit granted
        v
DynamicsProfileRuntimeManager resolves current Active Runtime
  CombinedExecutionLease(runtime lease + admission permit)
        |
        v
ControlledOperationExecutor
  linked caller + lease-loss + retirement CTS
  Client.ExecuteRegisteredOperationAsync(definition, request.Parameters)
        |
        v
DynamicsWebApiClient
  required parameter check
  server-owned template binding / scalar conversion
  outbound CRM HTTP
        |
        v
finally / await using
  runtime execution lease -> admission permit
  _inFlight, _totalAdmission, workload, active permit, reservation counters released
```

### Critical retention distinction

`OrganizationAdmissionManager` itself does not enqueue the parameter dictionary or `HttpContext`; its waiter state contains the semaphore wait, bounded workload string, envelope, linked cancellation state, counters, manager and immutable plan. This is good.

However, the complete awaiting chain still retains the original body graph:

1. `ControlledOperationExecutor.ExecuteAsync` receives `OperationExecutionRequest request`, awaits `_leaseProvider.AcquireAsync(...)` at lines 105-107, and uses `request.Parameters` again at lines 133-136. The generated async state machine therefore must strongly retain `request`, its dictionary and every `JsonElement` backing document during the full queue wait.
2. `DynamicsProfileRuntimeManager.AcquireAsync` retains only envelope/admission/plan while waiting and intentionally does not retain an old runtime (`DynamicsProfileRuntimeManager.cs:191-208`). This prevents generation retention, but cannot release the upstream request graph.
3. The `Program.cs` async endpoint awaits executor at line 197. ASP.NET owns `HttpContext` for the entire HTTP request, and the endpoint state machine has body/context parameters. Even after authorization is converted to server-owned strings, the request pipeline and likely endpoint state machine retain `OperationHttpRequest`, `HttpContext.User`, features and connection/request state until the response completes.
4. Gateway does not register or use ASP.NET Session (`AddSession`/`UseSession` are absent), and neither `OperationExecutionRequest` nor `DispatchEnvelope` copies `ClaimsPrincipal`, cookie, JWT or session. Thus there is no observed cross-request session cache here; the issue is request-lifetime retention, not a singleton session leak.
5. On the product side, `GatewayDynamicsOperationExecutor` also retains the original request plus a shallow-copied dictionary through `JsonContent` until the Gateway response arrives. A server rejection bounds server admission but does not currently give the client a pre-send byte guard.

## Existing Limits and Gaps

| Boundary | Existing limit | Exact behavior / gap |
| --- | --- | --- |
| Gateway inbound body | No project-defined limit in `Program.cs`, `appsettings.json` or `appsettings.Development.json` | Direct Kestrel/IIS deployments fall back to host defaults (commonly 30,000,000 bytes), while TestServer behavior is not an equivalent production proof. There is no single deployment-owned hard setting for Kestrel and IIS and no smaller operation-route contract. |
| Declared `Content-Length` | Only host default | No application test proves `limit + 1` is rejected before JSON deserialization/executor. |
| Chunked/unknown-length request | Only host default stream enforcement | No application test proves read stops at the first byte over the intended operation limit; `Content-Length`-only middleware would be bypassable. |
| JSON depth | `MaxDepth` is not set at `Program.cs:25-29`; System.Text.Json effective default is 64 | This is a parser safety ceiling, not a business parameter-graph contract. Depth within 64 can still carry a large nested object/array under one allowed parameter name. |
| Unknown JSON members | `UnmappedMemberHandling.Disallow` | Protects unknown DTO properties, but not graph size, parameter count/type/bytes, duplicate parameter semantics, or nested content under `Parameters`. |
| DTO parameter count | No explicit limit | Current registry has at most 3 declared parameters, but code scans every submitted key and materializes all unknown names with `.ToArray()` and `string.Join`, allowing avoidable allocation/error amplification before rejection (`ControlledOperationExecutor.cs:82-90`). |
| Declared parameter type / required | Registry declares them | Required/type validation occurs in `DynamicsWebApiClient` after queue admission (`DynamicsWebApiClient.cs:79-91`) or through ad hoc `Convert.ToString` encoders. A `JsonElement` object/array/null can reach admission even when the registry declares scalar string/guid/date-time. |
| Dispatch byte limit | Configured profile: `MaxDispatchEnvelopeBytes=65,536` (`appsettings.json:69`) | `EstimateEnvelopeBytes` is not exact: strings/keys use UTF-16 `Length * 2`, CJK is commonly 3 UTF-8 bytes per character, and every non-string—including arbitrarily large `JsonElement` object/array/string/number raw representation—is counted as 64 bytes (`ControlledOperationExecutor.cs:140-160`). |
| Dispatch hard upper bound | `[Range(256, 8_388_608)]` annotation | `OrganizationAdmissionPlan.TryCreate` only checks `< 256`; the manual binding path does not visibly invoke DataAnnotations, so the 8 MiB upper bound is not an executable guarantee here. |
| Canonical ordering/hash | Template hash only | No versioned canonical parameter bytes, type tags, sorted names, length prefixes or parameter hash exist. Dictionary insertion order can affect any future serialization-based fingerprint unless explicitly canonicalized. |
| Idempotency key | No length/byte/character validation | It is retained in request and envelope, and is included only via UTF-16 heuristic. Future write/idempotency work cannot safely use it before a bounded canonical rule exists. |
| Queue count | Configured `LocalQueueCapacity=48`; `AggregateMaxInFlight=24`, `MaximumRuntimeHosts=6` derives local in-flight 4; local total admission capacity is 52 | Enforced atomically by `_totalAdmission` plus `_inFlight`; per workload configured limit is 8. These are count bounds, not byte bounds for the request graphs retained by upstream state machines. |
| Queue wait | Configured 15 seconds; executor deadline is `QueueAdmissionTimeoutSeconds + 30` (45 seconds here) | Linked cancellation is bounded and disposed, but up to 48 queued async executions can each retain a body graph near the host request limit today. |
| Outbound lifetime | Configured 35 seconds | After permit, linked caller/lease-loss/retirement cancellation and `CancelAfter` bound CRM work. |
| Response body | ProductClient max default/configurable; CRM client max 2 MiB in current Gateway profile | Existing readers check declared length, read unknown length only to `max + 1`, dispose streams, and zero pooled buffers. These patterns should be copied for inbound body/prepared canonical buffers; they do not currently cap inbound requests. |

### True UTF-8 boundary

The current estimator is neither an upper bound nor a canonical measurement. Examples:

- ASCII `A`: UTF-8 1 byte, current estimator 2 bytes.
- Traditional Chinese `中`: UTF-8 3 bytes, current estimator 2 bytes, therefore undercounted.
- Emoji represented by a UTF-16 surrogate pair: UTF-8 4 bytes and `.Length * 2` also gives 4, but this accidental equality does not make the algorithm correct.
- `JsonElement` containing a 1 MiB object, array or string: current estimator adds 64 bytes.

The implementation must measure the exact bytes it actually owns, preferably by writing the versioned canonical representation into a bounded buffer and using its written length. `Encoding.UTF8.GetByteCount` is acceptable only when the exact same text/normalization/type representation is then encoded; a second divergent serializer is not.

## Queue Ownership, Cancellation, Disposal and Counters

### Current ownership that is already sound

- `DynamicsProfileRuntimeManager` resolves admission binding before queue wait and resolves the current runtime only after admission (`DynamicsProfileRuntimeManager.cs:191-246`). Queueing does not pin an old runtime/client/handler/token provider generation.
- `OrganizationAdmissionManager` reserves `_totalAdmission`, `_workloadCounts`, `_queued` and `_admissionReservations` in one lock-protected section (`OrganizationAdmissionManager.cs:210-244`).
- Queue wait uses a linked CTS composed from caller cancellation, manager admission stop and lease-loss token; the CTS is scoped with `using` and is disposed after wait (`OrganizationAdmissionManager.cs:246-260`).
- Cancellation, timeout, lease becoming unsafe and admission shutdown all call reservation cleanup; `_queued`, workload entry, total semaphore and drain signal are returned (`OrganizationAdmissionManager.cs:262-320,713-797`).
- Permit disposal is idempotent and returns in-flight, total admission, workload and reservation counts (`OrganizationAdmissionManager.cs:364-390,817-850`).
- Combined runtime/admission lease disposal attempts both resources and preserves both cleanup failures (`DynamicsProfileRuntimeManager.cs:923-954`).
- Manager shutdown stops admission, cancels queued waiters, waits reservations to drain, stops/awaits renewal, releases lease and disposes semaphores/CTS (`OrganizationAdmissionManager.cs:492-545`).

### Current gaps / credible retention or leakage risks

1. **Original request graph retained during queue wait:** the principal defect. The manager queue is metadata-only, but `ControlledOperationExecutor` retains `OperationExecutionRequest` because it uses `request.Parameters` after await.
2. **`JsonElement` backing storage has GC lifetime only:** Minimal API object deserialization produces `JsonElement` values. Those elements retain their backing JSON document/storage; the code has no `JsonDocument` owner to dispose and no deterministic zeroing path for inbound raw JSON bytes.
3. **No prepared buffer owner exists:** `DispatchEnvelope` is not disposable. There is no exact canonical buffer to zero, no owner transfer contract and no test hook proving cleanup on success, rejection, cancellation or exception.
4. **Count bounds do not imply byte bounds:** 48 queued requests can each retain a large single allowed `JsonElement`; `MaxDispatchEnvelopeBytes` currently accepts it as 64 bytes.
5. **Validation occurs too late:** missing required values and most declared-type failures consume queue/permit and can allocate template strings/URI data after admission.
6. **Unknown-name error amplification:** `.ToArray()` plus unbounded `string.Join` retains/echoes every unknown key until response serialization. Reject on parameter count/first bounded diagnostic instead.
7. **Raw parameter logging risk:** `DynamicsWebApiClient.cs:156-160` logs `logicalProfileId` as the submitted object. Until prepare enforces a bounded scalar, a complex/large `JsonElement` may be rendered by logging infrastructure. This is not a queue counter leak but is a downstream retention/export risk.
8. **Endpoint request-lifetime principal/context:** `HttpContext` and principal remain owned by ASP.NET until response completion. They are not copied into singleton/runtime/queue objects, so no cross-session leak was found; tests/documentation must avoid claiming they become collectible while the HTTP request is still pending.
9. **Gauge versus cumulative counters:** `Queued`, `InFlight`, `ActivePermits` and `TrackedWorkloadCount` must return to zero. `AcceptedCount`, `RejectedCount` and `TimeoutCount` are cumulative and must not be asserted as zero after activity. `_admissionReservations` is lifecycle-critical but not exposed in `AdmissionMetricsSnapshot`; either expose a bounded gauge for tests/readiness or prove it indirectly by successful drain/dispose and reacquisition.
10. **Leaked permit is still catastrophic:** normal executor paths dispose leases, but any future caller that acquires a permit and loses the owner will block manager shutdown and capacity. `PreparedOperationDispatch` must use the same single-owner/idempotent cleanup discipline.

## Existing Patterns to Follow

- **Declared length then `max + 1` streaming read:** `GatewayDynamicsOperationExecutor.ReadBoundedPayloadAsync` at `GatewayDynamicsOperationExecutor.cs:209-270` and `DynamicsWebApiClient.ReadBoundedJsonAsync` at `DynamicsWebApiClient.cs:458-503`.
- **Always dispose stream and clear rented bytes:** the same readers use `await using`, `CryptographicOperations.ZeroMemory` and `ArrayPool.Return` in `finally`.
- **Queue before runtime selection:** `DynamicsProfileRuntimeManager.cs:204-246` explicitly avoids retaining old generations while queued.
- **Reverse-order, attempt-all cleanup:** `DynamicsProfileRuntimeManager.cs:248-272,923-954`.
- **Cancellation-safe counter release:** `OrganizationAdmissionManager.cs:253-320,713-797`.
- **Baseline tests:** `OrganizationAdmissionManagerTests.cs:44-160` asserts queue/in-flight return to zero; `OrganizationAdmissionLeaseLifecycleTests.cs:49-70` proves dispose waits for permits.

## Minimal TDD Slice — RED First

Prefer new test files to avoid touching the AuthN/AuthZ agent's test files.

### A. Gateway inbound body tests

Create `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`.

1. `Declared_content_length_over_limit_returns_413_before_body_read_and_executor`
   - Configure the test limit to `N`.
   - Send `Content-Length=N+1` with content whose read/serialize path records or throws if touched.
   - Assert HTTP 413, executor call count 0, and body deserialization/reader not invoked.
   - Current RED reason: no project limit exists.
2. `Chunked_body_crossing_limit_by_one_byte_returns_413_and_disposes_stream`
   - HTTP/1.1 content with unknown length / `Transfer-Encoding: chunked`.
   - Yield exactly `N+1` bytes in small chunks.
   - Assert the server stops on the first over-limit byte, returns 413, executor 0, and request stream/content owner is disposed.
   - Current RED reason: no operation body counter/wrapper or explicit host limit test.
3. `Multibyte_utf8_body_accepts_exact_byte_limit_and_rejects_one_byte_over`
   - Build JSON using Traditional Chinese plus emoji and compute `Encoding.UTF8.GetByteCount(fullJson)`.
   - Equal byte count succeeds; the next full valid UTF-8 scalar that pushes the body over the limit returns 413.
   - Do not use `string.Length` in test setup except to demonstrate it differs.
4. `Json_graph_over_explicit_depth_is_rejected_before_executor`
   - Keep body bytes below the byte limit but nest JSON beyond configured `MaxDepth`.
   - Assert controlled 400, executor 0 and no queue counters change.

Test both focused body-reader behavior and at least one real Kestrel-hosted request. TestServer alone does not prove Kestrel/IIS server feature behavior. For chunked Kestrel proof, force HTTP/1.1 with unknown content length; avoid relying on HttpClient automatically choosing chunked.

### B. Canonical preparation tests

Create `SpeechMessage.Dynamics.Tests/OperationDispatchPreparerTests.cs`.

1. `Prepare_is_order_independent_and_emits_versioned_typed_length_prefixed_utf8`
   - Same typed parameters in different dictionary insertion orders produce identical bytes/hash.
2. `Prepare_uses_true_utf8_bytes_at_limit_minus_one_limit_and_limit_plus_one`
   - Exercise CJK/emoji boundaries; `limit-1` and `limit` accepted, `limit+1` rejected with `EnvelopeTooLarge`, no admission call.
3. `Prepare_rejects_deep_or_large_parameter_graph_before_admission`
   - Pass `JsonElement` object/array nested within the parser's accepted depth under an allowed scalar name.
   - Assert invalid parameter/type before admission; no fallback `Convert.ToString`.
   - Add a large scalar/array count case to prove parameter count and per-value byte bounds.
4. `Prepare_validates_required_count_name_type_and_idempotency_key_synchronously`
   - Missing required, extra parameter, wrong JSON value kind, oversized name/value/idempotency key all fail before the first await.
5. `Prepared_dispatch_dispose_zeros_canonical_buffer_and_releases_parameter_references`
   - Use `InternalsVisibleTo` or an injected test buffer/pool, not a production "dangerous buffer" API.
   - Assert idempotent dispose, entire written/rented region zeroed before return, typed parameter container cleared/released.

### C. Queue retention and cleanup tests

Create `SpeechMessage.Dynamics.Tests/OperationDispatchQueueLifecycleTests.cs` or add only the final soak assertion to `Phase4IsolationSoakTests.cs` after confirming ownership.

1. `Queue_wait_retains_prepared_dispatch_but_not_original_request_or_json_element_graph`
   - A blocking `IProfileExecutionLeaseProvider` captures only `DispatchEnvelope` and does not grant a lease.
   - A `[MethodImpl(MethodImplOptions.NoInlining)]` helper creates request/dictionary/valid scalar `JsonElement`, starts execution, returns weak references and the pending Task.
   - Force GC while queue wait is blocked; assert original request, source dictionary and JsonElement backing owner are collectible, while the pending execution still has only the bounded prepared object/envelope needed for later dispatch.
   - Current RED reason: executor must retain `request` because it reads `request.Parameters` after await.
2. `Queued_cancellation_disposes_prepared_dispatch_and_returns_all_gauges_to_zero`
   - Hold the sole real permit, start one prepared queued request, wait until `Queued==1`, cancel it, release holder.
   - Assert `Queued=0`, `InFlight=0`, `ActivePermits=0`, `TrackedWorkloadCount=0`; if added, `AdmissionReservations=0`.
   - Assert prepared buffer zero/return occurs exactly once and client was never called.
3. `Successful_and_exceptional_dispatch_both_dispose_prepared_dispatch_after_lease_cleanup`
   - Client success, client controlled failure, thrown exception and lease acquisition rejection must all zero/return the prepared buffer exactly once.
4. `Manager_dispose_cancels_queued_prepared_work_and_counters_drain_before_resource_disposal`
   - Start queued work, call `DisposeAsync`, assert cancellation completes, prepared owner disposes, gauges zero, renewal/slot cleanup completes.

## Minimal Production Shape Implied by the Tests

No implementation was made, but the smallest coherent change is:

1. A deployment-owned Gateway request limit option with a hard upper bound, applied to both Kestrel and `IISServerOptions`; set the same explicit JSON `MaxDepth` while preserving unknown-member disallow.
2. A synchronous `OperationDispatchPreparer.Prepare` called before the first executor await. It resolves the registry definition, enforces count/name/required/declared type/value bounds, converts `JsonElement` only to approved immutable scalar/array values, and writes one versioned sorted UTF-8 canonical representation.
3. A single-owner `PreparedOperationDispatch : IDisposable` containing only bounded server canonical routing metadata, immutable/bounded typed parameters, exact byte length/hash and the owned canonical buffer. It must not reference `OperationExecutionRequest`, `OperationHttpRequest`, `JsonDocument`, `JsonElement`, `HttpContext`, `ClaimsPrincipal`, credential/token/session or runtime/client.
4. `ControlledOperationExecutor.ExecuteAsync` prepares synchronously, stops using `request` before admission, queues only `prepared.Envelope`, dispatches `prepared.Parameters` after lease acquisition and disposes `prepared` in a `finally` covering rejection, cancellation, client result and exception.
5. Keep `IProfileExecutionLeaseProvider.AcquireAsync(DispatchEnvelope, ...)` unchanged unless an exact byte/hash field must be added to `DispatchEnvelope`; runtime selection-after-queue behavior is already correct.
6. Treat `EstimatedEnvelopeBytes` as exact written canonical bytes or rename it. A property named "estimated" must not remain the enforcement source.

## Recommended Non-Overlapping File Ownership

### Worker 1 — WebApi canonical preparation and executor lifecycle

Own exclusively:

- Create `SpeechMessage.Dynamics.WebApi/Runtime/PreparedOperationDispatch.cs`
- Create `SpeechMessage.Dynamics.WebApi/Runtime/OperationDispatchPreparer.cs`
- Modify `SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs`
- Modify `SpeechMessage.Dynamics.WebApi/Capacity/DispatchEnvelope.cs` only if exact canonical size/hash needs a contract field
- Create `SpeechMessage.Dynamics.Tests/OperationDispatchPreparerTests.cs`
- Create `SpeechMessage.Dynamics.Tests/OperationDispatchQueueLifecycleTests.cs`

Optional, only if tests require an observable reservation gauge:

- Modify `SpeechMessage.Dynamics.WebApi/Capacity/IOrganizationAdmissionManager.cs`
- Modify `SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs`

Do not modify existing Auth files or Auth tests.

### Worker 2 — Gateway hard body limit

Own exclusively:

- Create a new Gateway request-limit options/registration file, for example `SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayRequestBodyLimits.cs`
- Modify `SpeechMessage.Dynamics.Gateway/appsettings.json` for the production/default body limit; do not touch `appsettings.Development.json`
- Create `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`

### Unavoidable overlap: `SpeechMessage.Dynamics.Gateway/Program.cs`

`Program.cs` is currently modified by the AuthN/AuthZ agent and is the composition root for:

- Kestrel/IIS body-limit registration,
- explicit JSON `MaxDepth`, and
- any endpoint binding change required before deserialization.

Do not assign `Program.cs` concurrently. Finish/hand off the Auth agent first, then let the main agent or one designated integrator apply the small request-limit registration/binding patch on top of the settled Auth changes. Do not edit:

- `SpeechMessage.Dynamics.Gateway/Security/*`
- `SpeechMessage.Dynamics.Gateway/appsettings.Development.json`
- `SpeechMessage.Dynamics.Tests/GatewayKestrelNegotiateTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`

`ControlledOperationExecutorTests.cs` and `Phase4IsolationSoakTests.cs` are not currently Auth-owned, but new focused test files reduce merge risk and make the RED slice easier to review.

## Traditional Chinese Documentation Points Required by Spec

Every new production **and test** C# type needs substantive Traditional Chinese XML documentation. At minimum document:

- **Gateway body-limit type/options/registration:** deployment configuration is the trust source; Kestrel and IIS use the same byte setting/hard cap; declared and unknown-length bodies are both covered; rejection occurs before deserialization/executor; ASP.NET owns the request stream; no body, principal, session, token or cancellation registration is cached.
- **Request reader/middleware, if introduced:** it reads at most `limit+1`, counts actual wire bytes, preserves caller cancellation, does not close a stream it does not own unless the API contract transfers ownership, and releases/zeros any owned buffer in `finally`.
- **`OperationDispatchPreparer`:** synchronous prepare-before-await ordering is security-critical; registry is the type/name authority; exact version/type/sorted-name/big-endian-length-prefixed UTF-8 format; all failures occur before admission; no I/O or mutable global cache.
- **`PreparedOperationDispatch`:** unique single owner is `ControlledOperationExecutor`; list exactly what it may retain and prohibit `OperationExecutionRequest`, `JsonElement`, `HttpContext`, principal, user/session identity, token/credential/runtime/client; describe thread-safety, idempotent disposal, buffer zeroing and reference clearing order.
- **Modified `ControlledOperationExecutor.ExecuteAsync`:** original request is consumed only during synchronous prepare and not retained across queue wait; prepared buffer lives through queue/dispatch only; lease is disposed before prepared data; every return/exception/cancellation reaches cleanup.
- **Admission metric changes, if any:** distinguish instantaneous gauges from cumulative counters and explain why drain waits on reservations.
- **Test types/helpers:** explain why weak-reference/NoInlining is needed, which owner is expected to remain alive, which objects must be collectible, and that accepted/rejected cumulative counters do not return to zero.
- Add nearby Traditional Chinese comments at safety-order branches: Content-Length precheck before read, `limit+1` read, prepare before first await, runtime resolution after permit, runtime lease before permit release, zero before buffer return.
- All added/modified `.cs`, `.json`, `.md` files must be strict UTF-8 without BOM and CRLF; validate bytes/line endings before review.

## Risks

- **False sense of safety from server defaults:** 30 MB-class host defaults are far above a 64 KiB canonical queue budget and are not one explicit Kestrel/IIS/TestServer contract.
- **Content-Length-only fix is bypassable:** chunked or lying Content-Length requests require actual streaming enforcement.
- **Endpoint filter alone may be too late:** Minimal API arguments can be deserialized before a filter runs. The limit must be host-level or a custom binding/reader that executes before default JSON materialization.
- **TestServer-only proof is insufficient:** use real Kestrel for at least the declared/chunked integration boundary; separately configure IIS options and verify configuration binding.
- **Canonical format becomes a durable contract:** add a version byte and type tags now; never use ambiguous string concatenation or dictionary enumeration order.
- **Zeroing only the byte buffer is not enough to claim secret erasure:** typed managed strings remain GC-owned. Therefore accept only bounded non-secret operation parameters, clear owned containers on dispose, and never allow credentials/tokens in this envelope.
- **Weak-reference tests can be flaky:** isolate object creation in NoInlining helpers, remove test-local strong references, wait until the queue is definitely blocked, and use bounded GC retry rather than one collection.
- **Do not assert cumulative metrics return to zero:** only live gauges/reservations should return to baseline.
- **Program.cs merge conflict is active now:** body-limit integration must wait for the AuthN/AuthZ owner or be performed by that same owner after coordination.

## Conclusion

The admission manager's internal queue is count-bounded and does not directly store request bodies or runtimes. The release blocker is the upstream executor/request async chain: a body as large as the host accepts can be represented by one allowed `JsonElement`, estimated as 64 bytes, then retained throughout queue wait. The minimal safe slice is an exact pre-deserialization HTTP byte cap plus a synchronous registry-typed canonical preparer whose disposable bounded result is the only parameter state retained across admission.
