# Research: P7.3 Gateway JSON normalizer and metadata cache ownership

- Query: (1) How can GatewayOperationRequestBodyReader and existing normalizers materialize strictly closed `imagePayload` / metadata-target inputs without retaining `JsonElement`? (2) Which existing runtime/profile types provide `ProfileAlias` plus `GenerationId`, and where can a bounded metadata cache be composition-root owned without cross-profile/generation leakage?
- Scope: internal
- Date: 2026-08-12

## Findings

### 1. Gateway input boundary

`GatewayOperationRequestBodyReader` parses a bounded JSON body, checks duplicate property names recursively, then clones every non-null parameter `JsonElement` before disposing its `JsonDocument` and clearing/returning its pooled buffer. This protects the reader's buffer lifetime but deliberately leaves generic parameter values as `JsonElement`:

- `SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs:243-301` — `TryMaterialize` only accepts root `idempotencyKey` and `parameters`; it creates `Dictionary<string, object?>`, with each parameter stored as `parameter.Value.Clone()`.
- `SpeechMessage.Dynamics.Gateway/Program.cs:314-372` — authorization runs before body reading, then the resulting generic parameter dictionary is copied unchanged to `OperationExecutionRequest`.
- `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs:292-317` — current regression test intentionally proves the cloned parameter is still a `JsonElement` after pool zero/return.

The downstream Data8 executor already has the correct model: it copies each registered parameter before its first await, performs operation-aware normalization, and specifically rejects `JsonElement` for P7.3 image/metadata types:

- `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs:373-450` — closed registered-name set, required-field validation, and fresh executor-owned dictionary.
- `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs:465-529` — `image-payload` only accepts `ContactImageResponseData` and makes a second defensive byte copy; `metadata-optionset-target` only accepts a defined `MetadataOptionSetTarget` enum.
- `SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs:108-168` — the only P7.3 request types are `target:metadata-optionset-target` and `imagePayload:image-payload` (alongside simple `contactId`).
- `SpeechMessage.Dynamics.Abstractions/Operations/Package03SpecialResourceContracts.cs:73-146` — `ContactImageResponseData` owns a copied `byte[]`, while `MetadataOptionSetTarget` is a closed enum.

Recommended Gateway design: perform a new operation-aware materialization immediately after `Authorize(...)` succeeds and before `OperationExecutionRequest` construction / `RequestGuard` / executor admission. It may be a separate stateless `GatewayOperationRequestParameterNormalizer`, or `ReadAsync` may take the already authorized operation ID/definition. A separate normalizer avoids adding request identity/state to the singleton reader and is the smaller boundary change.

For the special-resource operations, the normalizer should iterate the registry-owned parameter definitions and return a new `Dictionary<string, object?>`; it must not pass any `JsonElement` onward.

- For either image update operation, accept exactly `contactId` plus `imagePayload`; `imagePayload` must be an object with exactly `imageBytes` (base64 JSON string) and `mediaKind` (exact string `Png` or `Jpeg`). Decode into a fresh bounded byte array, reject unknown/missing/null/wrong-kind fields and malformed/oversized base64, then construct `ContactImageResponseData`. Its constructor makes the defensive copy; the existing executor then enforces its 32 KiB, PNG/JPEG decoder, dimensions and pixel limits (`Data8ProfileOperationExecutor.cs:499-514`, `882-914`). Numeric enum values, MIME strings, caller-selected entity/field, stream/IFormFile and arbitrary object graphs remain rejected.
- For `metadata.optionset.retrieve.by.attribute`, accept exactly `target` as the exact JSON string `ContactCustomerTypeCode`, then materialize `MetadataOptionSetTarget.ContactCustomerTypeCode`. Do not admit entity, attribute, locale or cache-key strings. The Data8 helper maps this one enum to `contact.customertypecode` privately (`Package03Data8SpecialResourceOperations.cs:125-175`).
- Preserve current duplicate-name handling in the reader, but add operation-local exact member checking for the nested `imagePayload` object. Do not keep `GetRawText()`, a `JsonElement`, a `JsonDocument`, `Stream`, or `IFormFile` in the normalized request.

This is a necessary Gateway gap: with the current direct `Program.cs` copy, a JSON image payload reaches `TryNormalizeImagePayload` as `JsonElement` and is rejected, while a JSON metadata target likewise reaches `TryNormalizeMetadataOptionSetTarget` as `JsonElement` and is rejected. The existing typed Embedded/ProductClient path succeeds because it submits the typed `ContactImageResponseData` / enum directly.

### 2. Profile/generation identity and cache owner

The authoritative runtime identity type is `ResolvedProfile`:

- `SpeechMessage.Dynamics.Abstractions/Configuration/ResolvedProfile.cs:10-18` — immutable record contains `ProfileAlias` and `long GenerationId` (as well as internal organization/connector/credential data, which cache values/keys must not expose).
- `SpeechMessage.Dynamics.ControlPlane/Configuration/ConfigurationProfileResolver.cs:37-83,174-183` — builds immutable snapshots and assigns the supplied positive generation to each `ResolvedProfile`.
- `SpeechMessage.Dynamics.ControlPlane/Configuration/OfficialWorkerRuntimeProfileResolver.cs:50-94` — overlays the current active runtime generation into the resolved profile before routing.
- `SpeechMessage.Dynamics.ControlPlane/Runtime/ProfileRuntimeKey.cs:21-32` and `Runtime/IActiveProfileGenerationResolver.cs:20-31` — official-worker runtime key exposes `(ProfileAlias, Generation)`; it is a key/lookup seam rather than a metadata-cache owner.
- `SpeechMessage.Dynamics.Abstractions/Connectors/IConnectorPool.cs` and `IConnectorLease.cs` (contract copied in `.trellis/spec/backend/data8-generation-owned-connector-pool.md`) plus `SpeechMessage.Dynamics.Connectors.Data8/Data8ConnectorPool.cs:60-63,316-319` expose the same pair. A lease/pool must not own a long-lived cache because its lifecycle is connection admission/cleanup.

For Data8 P7.3, the suitable owner is the per-host, per-generation `Data8ProfileRuntime` composition root:

- `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileRuntime.cs:20-80` owns its pool registry, admission manager, resolver and `Data8ProfileOperationExecutor`; it creates one Data8 `ResolvedProfile` at generation 1 and disposes all owned resources in `DisposeAsync` (`85-111`).
- Dedicated Gateway wires that exact lifetime as one DI singleton and awaits disposal through `DedicatedData8RuntimeHostedService` (`SpeechMessage.Dynamics.Gateway/Program.cs:147-157`; `DedicatedData8RuntimeHostedService.cs:11-23`).
- ChurchReport Embedded wraps the same root in `EmbeddedData8Runtime` (`SpeechMessageProducts.ChurchReport/Services/EmbeddedData8Runtime.cs:22-65`), created in its nested composition root (`DonationDynamicsAccessBootstrap.cs:566-585`) and deterministically disposed with that provider (`594-613`, `774-783`).

Therefore inject a bounded cache service into the `Data8ProfileRuntime`-constructed executor (or make it an explicit runtime-owned collaborator) and always lookup with a typed key containing the complete resolved `(ProfileAlias, GenerationId, MetadataOptionSetTarget, locale-if-and-only-if-server-selected)`. The value should be an immutable copied array/list of `OptionSetOptionRecord`, never CRM `AttributeMetadata`, `OptionMetadata`, localized-label graph, `JsonElement`, request data, client, lease, or credential. Give it fixed entry/byte bounds, a short declared TTL, and deterministic removal/disposal when its runtime is disposed. A cache miss must query within the current lease; never cache a failure/partial response.

`Data8ConnectorPoolRegistry` already proves the generation rule but is not itself a sufficient cache owner: it routes only a matching active `(alias,generation)` (`Data8ConnectorPoolRegistry.cs:41-60,66-87`) and may retain one draining generation. If future configuration reload uses `Register` for a new generation, metadata entries for the retired generation must be explicitly removed at that same replacement/drain transition; the registry currently exposes no cache-retirement callback. Do not implement a process-wide cache until that retirement hook is explicit.

Do not reuse ChurchReport's legacy cache:

- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:41-42,580-595` has a static `FallbackOptionSetMetadataCache` used with the legacy ToolUtility service. It has neither profile/generation partitioning nor composition-root disposal/bounds visible at this boundary and is unsuitable for the P7.3 Gateway/Data8 cache.

### Tests to extend or add

- Extend `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs` with direct normalizer/endpoint cases for accepted `imagePayload` and metadata target; unknown/missing/duplicate nested image members, non-string/numeric enum, base64 failure, zero/over-limit decoded bytes, and no `JsonElement` surviving in the executor-observed request. Preserve its current buffer-zero and cancellation assertions.
- Extend `SpeechMessage.Dynamics.Tests/Data8ProfileOperationExecutorTests.cs:503-702` for the Gateway-normalized typed image/metadata values and mutation-after-normalization defensive-copy assertion. Existing tests already exercise special-resource response branches and invalid signature/dimension payloads.
- Add a focused metadata-cache test (new test file is preferable) with two distinct aliases/generations and distinguishable option labels: assert hit only for the exact typed key, miss after generation replacement, no raw SDK object retention, TTL/entry/byte eviction, and concurrent A/B calls do not cross-return values.
- Extend `SpeechMessage.Dynamics.Tests/Data8ConnectorPoolTests.cs:289-313` or runtime-level tests with cache retirement coordinated with existing generation replacement/drain proof.
- Extend `SpeechMessage.Dynamics.Tests/Data8ProfileRuntimeTests.cs:24-43` and `ChurchReport.MemberInfo.Tests/EmbeddedData8RuntimeTests.cs:23-66` to prove cache ownership dies with its Dedicated/Embedded runtime and does not create a connector client merely by construction.

## Related specs

- `.trellis/spec/backend/cross-user-isolation-and-performance.md` — cache keys must use the full validated isolation boundary; profile/generation mismatch is a cache miss and all entries need bounds, expiry and invalidation.
- `.trellis/spec/backend/data8-generation-owned-connector-pool.md` — Data8 runtime/pool isolation is `(ProfileAlias, GenerationId)`; retired generations drain and cannot serve new work.
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` — profile isolation and composition-root lifecycle requirements.
- `.trellis/spec/guides/cross-user-isolation-and-performance-review.md` — required A/B cache-isolation, cancellation, eviction and baseline checks.

## Caveats / Not Found

- No P7.3 ProductClient interface/implementation exists yet; the current generic `GatewayDynamicsOperationExecutor` serializes `Dictionary<string, object?>` unchanged (`SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs:108-126`). The Gateway parser must match the JSON shape that the forthcoming typed ProductClient emits.
- No existing bounded, profile/generation-aware metadata cache or generation-retirement callback was found in the Dynamics Gateway/Data8 runtime. The legacy static ChurchReport cache is specifically unsafe to adapt.
- The ChurchReport Embedded request guard presently allows only P7.2 IDs (`DonationDynamicsAccessBootstrap.cs:575-584`); P7.3 IDs must be added in the owning implementation task before Embedded P7.3 calls can reach the executor.
