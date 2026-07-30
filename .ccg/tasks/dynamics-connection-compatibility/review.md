# Review: Dynamics Access Gateway SPEC

## Final status

Planning artifacts are ready for user/spec review. No production implementation
has started.

The final design now covers:

- direct no-SDK HTTP/OData v4 access for Dynamics 365 CE 8.2 and 9.1;
- a default shared Gateway Web Service plus an approved Embedded host adapter
  selectable only through trusted product JSON;
- Visual Studio development support through local Gateway or Embedded fake-server
  profiles without production secrets;
- service-identity-only warm-up for connection/metadata latency, never a
  per-user CRM session or LINE/account keyed pool;
- organization-wide admission control shared across Gateway and Embedded hosts;
- zero-tolerance release gates for session/profile/token/credential/cache and
  resource leakage;
- final no-SDK migration and enforcement gates.

## Review evidence

### Official research

Official Microsoft documentation and community notes were reviewed and recorded
in the planning artifacts. Key conclusions:

- Dynamics CE Web API is OData v4 and can be called with direct authenticated
  HTTP without a language-specific CRM SDK assembly.
- CE on-prem 8.2 and 9.1 must be treated as explicit routes/capability profiles;
  route/CSDL validation is not the same as proof of exact CE product release.
- CE on-prem client-secret/certificate client credentials must not be claimed;
  IFD/OAuth is a target-specific feasibility gate.

### CCG dual-model reviews

Full Gemini + Claude review succeeded without quota fallback:

- 20260723-130809-dynamics-access-gateway-spec-final-closure-reviewer
- 20260723-144408-dynamics-access-gateway-spec-host-mode-final-review-reviewer
- 20260723-153235-dynamics-access-gateway-spec-final-completeness-review-reviewer
- 20260723-165816-dynamics-access-gateway-spec-final-postpatch-review-reviewer
- 20260724-083105-dynamics-access-gateway-spec-final-convergence-review-reviewer
- 20260724-084725-dynamics-access-gateway-spec-final-closure-after-warnings-review-reviewer

The host-mode final review returned no Critical findings. It identified one
Warning: `MaxDispatchEnvelopeBytes` was referenced in the aggregate queue
memory formula but not explicitly part of the JSON/admission schema.

Fix applied:

- added `LocalQueueCapacity` and `MaxDispatchEnvelopeBytes` to
  `OrganizationAdmissions`;
- made `MaxDispatchEnvelopeBytes` manager-owned and deployment-bounded;
- required canonical dispatch-envelope byte-size rejection before queueing;
- defined worst-case queued payload bound as
  `MaximumRuntimeHosts * LocalQueueCapacity * MaxDispatchEnvelopeBytes`;
- updated the reviewer prompt regression checklist accordingly.

The 20260723-165816 post-patch review completed with both Gemini and Claude,
without quota fallback. Gemini returned PASS. Claude identified two Critical and
two Warning documentation gaps; all were corrected before final local
verification:

- replaced the ambiguous environment-derived `OrganizationAdmissionKey` model
  with distinct `CanonicalOrganizationCapacityKey`, entry-resolved
  `OrganizationAdmissionKey`, and `RuntimeHostSlotLeaseNamespace` definitions;
  no raw environment tuple can own a separate budget, queue, permit, or lease;
- made server-owned FetchXML/OData template encoding explicit and added hostile
  value/injection tests for XML, OData, URI, and multipart contexts;
- mapped the design rollout stages to implementation Phases 0-6 explicitly;
- expanded the Organization-call coverage matrix into an enforceable CI
  completeness gate with version evidence, parameter encoding contexts, audit,
  migration, owner, and deadline fields.

### Additional read-only audits

Two focused read-only audits completed before the final updates:

- Safety audit found the lease-expiry, admission-epoch, operation-revision,
  audit-intent, queue-scope, reload-churn, warm-up, and user/LINE retention gaps.
- Compatibility audit found the organization base URI/path, route-versus-release
  language, IFD feasibility, strict JSON validation, final no-SDK wording, queue
  policy subject, and warm-up wording gaps.

All substantive findings were incorporated into the PRD, design, implement plan,
assessment, and user-facing SPEC.

The final read-only spec-gap audit also completed without Critical findings. Its
three suggested clarifications (canonical capacity key, Embedded trust artifact,
and CI gate matrix) were incorporated before the post-patch dual-model review.

The final convergence review found no Critical issue. Its remaining Warning
items were resolved and re-reviewed:

- Visual Studio Embedded fake-profile testing now has its own non-production
  trust anchor (approved local development registry or signed Development
  manifest); it cannot authorize production identities/secrets/endpoints and
  remains NotReady when its development trust artifact is absent or invalid.
- The Organization-call coverage matrix is now versioned machine-readable data;
  CI matches each migrated row's capability ID, typed parameters, encoding
  contexts, version evidence, and audit/idempotency class against the generated
  operation registry.
- Compression remains disabled by default. It can be enabled only after
  profile-gated real-target throughput/CPU/p95 evidence plus bounded streaming
  decompression and hostile-content tests.

The final closure review
`20260724-084725-dynamics-access-gateway-spec-final-closure-after-warnings-review-reviewer`
completed with Gemini and Claude, no quota fallback, no Critical findings, and
no remaining Warning findings. Both reviewers recommended PASS.

Two extra final read-only audit agents were later started after the
`MaxDispatchEnvelopeBytes` fix but exceeded the wait window and were
interrupted. They were read-only and made no file changes. The formal
Gemini+Claude review plus local checks are the acceptance evidence.

## Local verification

Fresh checks completed after the final fixes:

- UTF-8 validation passed for PRD, design, implement plan, assessment, SPEC, and
  CCG reviewer prompt.
- Markdown fence balance passed for the same files.
- Trailing whitespace scan passed.
- JSON examples parsed successfully: 7 JSON blocks.
- Requirement marker coverage passed for Gateway/Embedded, Visual Studio,
  warm-up, LINE/session exclusion, RuntimeHostSlotLease, AdmissionEpoch,
  AuditIntent, OperationDefinitionRevision, LocalQueueCapacity, and
  MaxDispatchEnvelopeBytes.
- Post-review marker coverage also passed for canonical physical-organization
  capacity, distinct lease/admission namespaces, signed Embedded trust artifact,
  FetchXML/OData encoding contexts, hostile-value tests, rollout phase mapping,
  coverage-matrix CI completeness, and the CI gate matrix.
- Final closure verification passed for the non-production Embedded trust anchor,
  machine-readable matrix-to-operation-registry validation, profile-gated safe
  compression, UTF-8, markdown fences, trailing whitespace, JSON examples, and
  stale admission/lease-key wording.
- Obsolete design term scan passed for the prior gateway-replica,
  replica-lease, base-URL, normalized-origin, and placeholder-auth terminology.

## Remaining gates before implementation

Implementation must not start until user/spec review accepts the planning
artifacts. Before production use, the implementation still needs:

- real CE 8.2 and 9.1 target smoke tests;
- target-specific Windows/IWA or AD FS OAuth feasibility proof;
- fake-server isolation and leak tests;
- Gateway/Embedded mode contract and JSON validation tests;
- soak/load tests with bounded memory/socket/queue/timer growth;
- final no-SDK CI enforcement after legacy migration.

---

## 2026-07-28 Phase 4 local isolation-hardening follow-up

### Review evidence

- `20260728-143828-dynamics-phase4-isolation-hardening-reviewer`: Gemini and
  Claude completed through the self-healing runner with no Critical finding.
- `20260728-150858-dynamics-phase4-final-isolation-hardening-reviewer`: both
  backends completed. The bounded ADFS body/handler lifecycle observations were
  implemented and covered by red/green tests.
- `20260728-152209-dynamics-phase4-final-buffer-zeroing-reviewer`: both
  backends completed. It requested deterministic host-slot release ownership.
- `20260728-153906-dynamics-phase4-final-lease-lifecycle-reviewer`: both
  backends completed with `ok=true`, `degradedFallback=false`, and
  `quotaBlocked=false`. No Critical finding; it warned that direct synchronous
  waiting could capture a caller synchronization context.
- `20260728-155852-dynamics-phase4-final-completion-reviewer`: both backends
  completed with `ok=true`, `degradedFallback=false`, and `quotaBlocked=false`.
  Both reported PASS with no Critical or Warning finding in the local Phase 4
  hardening scope.

### Resolution

- `RuntimeHostSlotLease.Dispose()` no longer fire-and-forgets slot release. Its
  compatibility path waits deterministically and runs the asynchronous release
  off the caller synchronization context; `DisposeAsync()` remains the normal
  `await using` path.
- Regression coverage proves blocking release, caller-context isolation, and
  release-failure propagation.
- The ADFS successful-token parser now uses the bounded rented buffer directly,
  zeros it before return, and avoids an extra managed response-body copy.
- The reusable rule is recorded in
  `.trellis/spec/backend/quality-guidelines.md` under **Async Capacity Lease
  Cleanup**.

### Status

No Critical finding remains in the local hardening scope. Full production Phase
4 remains blocked by the documented durable-coordinator, profile lifecycle,
workload-authentication, soak, and authenticated CE 8.2/9.1 matrix gates.

---

## 2026-07-29 Traditional Chinese discussion-guide consolidation

### Scope

Expanded `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` so the
explanation manual records the complete user discussion, not only the final
architecture. The new decision record covers:

- legacy CRM SDK strengths and limitations;
- direct Web API and official SDK replacement choices;
- official NuGet provenance versus the checked-in third-party Data8 project;
- Central Gateway, Local Gateway, and deferred Embedded responsibilities;
- product JSON, Gateway profile/registry, and secret-provider ownership;
- process-local physical pools versus organization-wide admission;
- preserved Phase 4/5/6 work and explicit Data8 removal gates;
- session/resource isolation and safe sustained-performance requirements.

### Review evidence

The review ran through the required self-healing CCG entrypoint:

- Run: `20260729-131330-dynamics-gateway-discussion-guide-reviewer`
- Gemini: completed with usable output.
- Claude: provider session quota blocked; no usable output.
- Runner state: `degradedFallback=true`, `fallbackAccepted=true`,
  `quotaBlocked=true`.

This is a single-model degraded review, not a successful dual-model review.
Gemini reported no Critical findings and one Warning: the localhost Gateway
port example could conflict with the workspace's actual launch settings.

### Resolution

- Replaced the guide's localhost HTTPS example with the current
  `SpeechMessage.Dynamics.Gateway/Properties/launchSettings.json` value,
  `https://localhost:7244/`.
- Added an explicit note that the port is deployment configuration rather than
  part of the REST contract and that `launchSettings.json` is authoritative for
  the current local workspace.
- Did not adopt the reviewer's speculative ADFS registration explanation;
  the guide retains only evidence already established by the project probes.

No Critical or known Warning remains in the updated explanation-guide scope.

---

## 2026-07-29 Local/Central Gateway product-boundary implementation

### Scope and result

Implemented the first Local/Central Gateway milestone without changing product
traffic or adding a new execution-mode enum:

- strict startup validation for Gateway topology, alias, API prefix, inactive
  Embedded configuration, and bounded response size;
- deployment-configured profile pinning before HTTP send;
- `ResponseHeadersRead` plus one hard Content-Length/chunked response limit;
- deterministic response/stream disposal and cleared rented/temporary buffers;
- preserved caller cancellation and sanitized transport/read logging;
- minimal `System.Security.Cryptography.Xml 10.0.9 -> 10.0.10` security patch.

Fresh local evidence:

- `ProductModeOptionsTests`: 26/26 passed after the recorded RED stage.
- `GatewayProductClientTests`: 7/7 passed after the recorded RED stage.
- complete Dynamics test project: 125/125 passed.
- solution Release build: 0 warnings, 0 errors.
- Data8 NuGet audit: no vulnerable package reported after the patch.
- UTF-8/no-BOM/CRLF, `git diff --check`, added-line secret scan, and shared
  mutable session/token/client scan passed.

### External review

- Run: `20260729-135309-dynamics-local-central-boundary-implementation-reviewer`
- Gemini: completed, no Critical finding, recommended PASS.
- Claude: provider session quota blocked, no usable output.
- Result: degraded single-model fallback; this is not full dual-model success.

Gemini's only Warning was the known Data8/WS-Trust legacy debt. It remains valid
and is already constrained by the executable SPEC: Data8 is temporary, must be
isolated behind a recyclable worker before load, and is removed only through the
Phase 6 gates. No implementation-specific Critical or unresolved Warning was
reported.

Detailed evidence is recorded in
`.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-local-central-boundary-verification.md`.

---

## 2026-07-29 Multi-Profile Runtime drain recovery closure

### Initial implementation review

The formal self-healing dual-model run completed without fallback:

- Run: `20260729-161900-dynamics-multi-profile-runtime-reviewer`
- Gemini: PASS, no Critical or Warning.
- Claude: one valid Critical in `DynamicsProfileRuntimeManager.ReplaceCoreAsync`.

The Critical was that a published replacement whose old runtime threw from
`DrainAndDisposeAsync` could leave `slot.Draining` permanently populated. Later
replacements were rejected forever. Clearing the reference unconditionally was
also unsafe because caller cancellation or timeout can leave the old runtime
still live and holding execution/resource ownership.

### Dual-model analysis and TDD resolution

The repair was designed through a second full dual-model run:

- Run: `20260729-163834-dynamics-multi-profile-runtime-drain-recovery-analyzer`
- Result: `ok=true`, `degradedFallback=false`, both Gemini and Claude completed.

The manager now:

- uses `ReplacementInProgress` as the single alias-level asynchronous owner;
- retries a prior unfinished Draining runtime before generation allocation or
  factory invocation;
- clears only the exact `slot.Draining` reference whose runtime state is
  `Disposed`, while still propagating cleanup errors;
- retains unfinished Draining ownership after cancellation/timeout;
- uses caller plus manager-shutdown linked cancellation for published drain;
- delays generation numbering and third runtime allocation until the previous
  Draining runtime has converged.

RED-to-GREEN regressions cover disposed cleanup failure, unfinished drain
recovery, and manager-shutdown cancellation of a published replacement owner.

### Re-review and real-runtime coverage warning

The first repair re-review also completed with both backends and no fallback:

- Run: `20260729-170800-dynamics-multi-profile-runtime-drain-recovery-reviewer`
- Gemini: PASS, no Critical or Warning.
- Claude: no Critical; one valid Warning that manager tests used a fake runtime
  without production `_drainTask` caching/reset semantics.

The Warning was resolved with
`Manager_retries_the_real_runtime_after_cancelled_drain_without_allocating_a_third_generation_early`,
which composes the real `DynamicsProfileRuntimeFactory` and
`DynamicsProfileRuntime`. Removing production `_drainTask = null` failure reset
made the test RED with Aggregate/TaskCanceled failure; restoring it made the
test GREEN and returned the admission registry entry count to zero.

### Final closure review

- Run: `20260729-172452-dynamics-production-runtime-retry-integration-reviewer`
- Result: `ok=true`, `degradedFallback=false`, `quotaBlocked=false`.
- Gemini: PASS, no Critical or Warning.
- Claude: PASS, no Critical or Warning.

Gemini included an Info-only suggestion to use UTF-8 with BOM. It was not
adopted because the user requirement and repository `.editorconfig` require
UTF-8 without BOM plus CRLF. The final local encoding gate enforces that
contract strictly.

### Final local evidence for this increment

- Dynamics tests: 159 passed, 0 failed, 0 skipped.
- Focused Multi-Profile/Registry/Factory/Readiness/Phase4 soak: 36 passed.
- Solution Release build: 0 warnings, 0 errors.
- Data8 NuGet vulnerability audit: no known vulnerable packages reported.
- Changed-file scoped format, strict UTF-8/no-BOM/CRLF, Traditional Chinese
  documentation scan, and `git diff --check`: passed.

This closes the local Multi-Profile drain-recovery Critical and review Warning.
It does not close the overall task or Phase 4: real Local Gateway/ChurchReport,
authenticated CE 8.2/9.1, durable cross-process coordination, fault/soak/
performance, Phase 5 migration, and Phase 6 Data8/SDK removal remain open.

---

## 2026-07-29 Gateway inbound-body and canonical-queue review

### Implemented scope

- Added one deployment-owned hard inbound body limit shared by Kestrel, IIS,
  and the application reader.
- Preserved authentication and principal→workload→alias→operation
  authorization before every Content-Type, body-length, body-I/O, JSON, and
  executor boundary.
- Added a fail-closed JSON-only contract: case-insensitive
  `application/json`, with no parameter or exactly one UTF-8 charset; all
  missing, malformed, `application/*+json`, unknown/repeated-parameter, and
  non-UTF-8 cases return 415 before body I/O or buffer rent.
- Added bounded UTF-8 wire-byte, JSON depth, duplicate/unknown-member, and
  chunked limit+1 enforcement with full rented-array zeroing before Return.
- Added canonical prepared dispatch so the queue retains only bounded detached
  scalar state and exact canonical bytes, never the original request,
  dictionary, `JsonElement`, `JsonDocument`, `HttpContext`, principal, session,
  token, or credential graph.
- Added one-owner, concurrent-idempotent `PreparedOperationDispatch.Dispose`
  with lease cleanup ordered before prepared-buffer cleanup.

### TDD and local evidence

- The new unsupported-media-type theory was first observed RED: all six initial
  cases returned 200 instead of the required 415.
- Focused Gateway request-body boundary tests: 24 passed, 0 failed.
- Full `SpeechMessage.Dynamics.Tests` Release: 227 passed, 1 live-SQL test
  skipped by contract, 0 failed.
- Full solution Release build: 0 warnings, 0 errors.
- Full solution tests excluding one unrelated pre-existing RichMenus
  root-detection test: all passed. The excluded test searches for the absent
  `ChurchReport.sln` instead of the actual `SpeechMessageProducts.sln` and is
  not changed by this task.
- Strict UTF-8 without BOM, CRLF-only, final CRLF: passed for the 15 scoped
  source/config/test/spec/review-input files.
- Scoped `dotnet format --verify-no-changes`, `git diff --check`, literal-secret
  scan, private-key/bearer scan, and `<inheritdoc />`-only scan: passed.
- `Package01FeeReadsEnabled=false` remains unchanged.

### CCG dual-model review

The first review invocation exceeded the outer 10-minute wait after Gemini
produced output and before Claude produced stdout. It was not accepted as a
review result. The required retry completed through the project self-healing
entrypoint:

- Run: `20260729-214756-gateway-http-canonical-final-review-retry-reviewer`
- Result: `ok=true`, `degradedFallback=false`, `quotaBlocked=false`
- Gemini: completed with usable output.
- Claude: completed with usable output.

Both reviewers reported no Critical finding in the Gateway inbound-body,
canonical queue, ownership, cancellation, or cleanup scope. Both found no
Session/Memory/Resource Leakage in the new Gateway and WebApi code.

Claude reported one repository-hygiene Warning: root-level untracked
`review_diff.patch`. It was preserved and moved to
`.ccg/dual-model-runs/legacy-review-diff-20260729.patch`, so it can no longer be
accidentally committed as a root production artifact.

Gemini reported a LINE lifecycle Warning: many existing
`LineMessagingClient` methods create `HttpRequestMessage` and
`HttpResponseMessage` without deterministic disposal. Source inspection
confirmed that this is real pre-existing production debt, but the current diff
changes only the `MarkAsReadByTokenAsync` XML documentation and two whitespace
characters; it did not create or expand those request/response paths. This
finding remains a separate zero-tolerance LINE lifecycle remediation gate and
must receive its own task, TDD coverage, full Traditional Chinese ownership
documentation, and focused soak/handler assertions before the LINE client is
declared lifecycle-clean. It is not silently treated as an optimization or as
resolved by this Gateway review.

Info-only observations not adopted in this increment:

- An `application/json` fast path could avoid media-type parser allocation, but
  no measured performance regression exists; correctness remains preferred
  until a benchmark proves the change useful.
- The negative `ContentLength` branch is defense-in-depth because real ASP.NET
  Core parsing normally exposes invalid/negative lengths as null.
- Options validation occurs at more than one fail-closed boundary; this is
  redundant but not a correctness or lifecycle defect.

### Status

The Gateway inbound-body and canonical-queue increment has no remaining
Critical or Gateway-specific Warning. The overall Dynamics task remains open
for ChurchReport Local Gateway integration, AD FS/PKCE, authenticated CE 8.2
and CE 9.1 evidence, durable coordination, soak/performance, Phase 5 migration,
and Phase 6 Data8/SDK removal. The separate pre-existing LINE response/request
disposal finding is recorded as an overall repository lifecycle blocker rather
than being hidden inside this Gateway increment.

---

## 2026-07-29 Gateway-owned success endpoint disclosure review

- TDD RED confirmed the real defect: serializing `OperationExecutionResult.Data`
  exposed `approvedWebApiRoot`.
- The minimal Production change removed only that Gateway-owned output field;
  outbound URI allowlisting, authentication, cancellation, retry, and resource
  disposal were unchanged.
- Focused `DynamicsWebApiClientTests`: 17 passed, 0 failed, 0 skipped.
- Full `SpeechMessage.Dynamics.Tests` excluding the explicit opt-in live SQL
  case: 228 passed, 0 failed, 0 skipped.
- Full `SpeechMessageProducts.sln` Release build: 0 warnings, 0 errors.
- Added Production and test comments satisfy the required deep Traditional
  Chinese trust-boundary, ownership, cancellation/cleanup, and performance
  documentation contract.
- Source/test encoding: UTF-8 without BOM, CRLF-only, final CRLF.
- Full CCG run
  `20260729-223644-dynamics-endpoint-disclosure-final-review-reviewer` completed
  with `ok=true`, `degradedFallback=false`, `quotaBlocked=false`; Gemini and
  Claude both returned PASS with no Critical or Warning.
- Remaining independent gate: upstream `@odata.context`/`@odata.nextLink`
  absolute URLs must be projected or consumed server-side before a production
  operation is allowed to expose raw OData payloads.

This closes the explicit Gateway-added `approvedWebApiRoot` defect only. The
overall task remains in progress for ChurchReport lifecycle ownership, Local
Gateway E2E, real CE 8.2/9.1, soak/performance, Phase 5, and Phase 6.

## 2026-07-29 ChurchReport Local Gateway session-lifecycle review

### Implemented scope

- Primary-DI ownership for the ChurchReport Dynamics ProductClient process generation.
- Bounded fail-closed Gateway WhoAmI startup preflight with strict disabled/Embedded no-op behavior.
- Deterministic Donation manager/processor cleanup for self-owned LINE and semaphore resources.
- Opaque Session resource scope, request ref-counted lease, cache/logout/re-login/host drain, and failed-cleanup retry ownership.
- Real Logout action and re-login initialization coverage, not only private-helper reflection tests.
- Approved response-lifecycle lease return for legacy manually constructed contexts.

### Independent spec review and resolution

The read-only Trellis reviewer initially returned FAIL with three Critical findings:

1. a no-slot logout race could allow an earlier request to publish under the retired scope;
2. stale cache detection could publish a generation on an already removed slot;
3. resource Dispose failure could decrement Active to a false zero and lose retry ownership.

Each finding was verified against the codebase, reproduced with a deterministic failing test, and fixed individually. Local code-quality review additionally found and fixed the pre-publication host-stop cleanup-owner gap. No production fallback owner, credential output, endpoint disclosure, or feature-flag enablement was added.

### Fresh local evidence

- Session lifecycle focused suite: 23 passed, 0 failed.
- Authentication drain paths: 4 passed, 0 failed.
- Full ChurchReport tests: 366 passed, 0 failed, 0 skipped.
- Dynamics non-live tests: 228 passed, 0 failed, 1 skipped live SQL test.
- Release solution build: 0 warnings, 0 errors.
- `Package01FeeReadsEnabled=false` remains unchanged.
- Embedded, Data8, and the checked-in `PowerPlatform.Dataverse.Client` project remain present.

### Final local and external review gates

- Scoped product/test `dotnet format --verify-no-changes`: passed.
- Strict UTF-8 without BOM, CRLF-only, final-CRLF gate: passed for the final 23 scoped files, including the final review prompt and task evidence updates.
- `git diff --check`: passed.
- Sensitive literal assignment scan: 0 matches.
- CCG run: `20260730-011623-churchreport-local-gateway-session-lifecycle-final-review-reviewer`.
- Gemini completed and returned PASS with no Critical or Warning finding.
- Claude produced no review because its provider session quota was blocked until the reported reset time.
- Runner state: `ok=false`, `quotaBlocked=true`, `degradedFallback=true`, `fallbackAccepted=true`, completed backend `gemini`, failed backend `claude`.

This was an accepted single-model degraded fallback under the project self-healing policy. It was not a completed Gemini+Claude dual-model review and still requires a later full retry. At the time of this increment, real Local Gateway/CE/browser evidence remained outside its scope; the following 2026-07-30 section records the later Local Gateway and browser slice without claiming real CE completion.

---

## 2026-07-30 Development Local Gateway configuration, browser, and AD FS probe review

### Implemented and verified scope

- Gateway Development configuration now selects the explicitly provisioned same-user LocalDB instance, dedicated `SpeechMessageDynamicsControlPlane` database, integrated authentication, bounded pool 32, and five-second connect timeout.
- The Development CRM target remains intentionally non-routable and fail closed. The allowed operation returns a controlled sanitized failure without transport, alias, Embedded, Data8, Central, or production fallback.
- ChurchReport Development selects Gateway mode, `crm82`, CE 8.2, HTTPS loopback, and `/v1`; `Package01FeeReadsEnabled=false` remains unchanged, so no Package 1 consumer traffic or preflight resource graph is enabled.
- The historical AD FS token probe is now a retired fail-closed entrypoint. It accepts no credential/token/result parameters, reads no appsettings, performs no network/file work, and creates no background owner.

### Fresh runtime and browser evidence

- Opt-in live LocalDB durable coordinator contract: passed.
- Real Development Gateway: `/health` 200, `/ready` 200, anonymous `/v1` 401, authorized Windows workload catalog 200, wrong alias 403, unauthorized operation 403, and the only allowed operation against the non-routable target returned controlled 400 with no fallback.
- ChurchReport and Local Gateway ran concurrently. ChurchReport root returned 200; the in-app Browser login page reached `readyState=complete` with zero JavaScript errors. Two pre-existing DevExtreme deprecation warnings are outside this slice.
- Both hosts were stopped and localhost listeners 5080 and 7244 were released.
- Read-only WinRM/Negotiate AD FS verification proved one Public Client, one callback, and the approved shared-IFD/Gateway/fail-closed markers without persisting or printing their values.

### Local quality evidence

- Dynamics tests: 230 passed, 0 failed, 1 ordinary-run environment skip; the skipped LocalDB live contract passed separately.
- ChurchReport tests: 367 passed, 0 failed.
- Release solution build: 0 warnings, 0 errors.
- Changed-file format, strict UTF-8 without BOM/CRLF/final-CRLF, `git diff --check`, and added-line sensitive-literal scans passed.
- A provider Session marker observed in one Claude artifact was removed immediately; the remaining artifact marker scan returned zero. No value is retained in this review.

### Full CCG result

Run `20260730-022825-local-gateway-development-config-adfs-probe-final-review-reviewer` completed with:

```text
ok=true
degradedFallback=false
quotaBlocked=false
Gemini=PASS
Claude=PASS
```

Both reviewers confirmed the bounded Development slice with no Critical lifecycle, credential-disclosure, fallback, or configuration finding. The real CE 8.2/9.1, Phase 5, and Phase 6 gates remain open and were not misclassified as completed.

### Remaining Warning and open gates

- .NET Configuration arrays merge by numeric index. The Development Windows workload binding is appended after the base AppPool binding rather than strictly replacing every inherited entry. The AppPool identity does not exist locally, so there is no demonstrated privilege escalation in this environment, but explicit inherited-index replacement/neutralization remains a hardening item.
- Real CE 8.2/9.1 WhoAmI, authentication, operation matrix, rollback, OData annotation projection, cross-process capacity, coordinator-fault behavior, soak/performance, and shutdown baseline still block Phase 4 completion.
- Phase 5 has not yet migrated one isolated ChurchReport workflow; the broad Package 1 flag must remain false.
- Phase 6 remains report-only. Embedded, Data8, and the checked-in `PowerPlatform.Dataverse.Client` project remain retained.

---

## 2026-07-30 Complete ChurchReport lifecycle and documentation dual-model retry

### Runner result

The required retry completed through the self-healing entrypoint:

```text
20260730-024616-churchreport-local-gateway-documentation-lifecycle-final-review-reviewer
ok=true
degradedFallback=false
quotaBlocked=false
completedBackends=gemini,claude
```

This closes the earlier provider-quota gap: both Gemini and Claude produced usable lifecycle/documentation review output.

### Cross-model finding reconciliation

- Claude returned PASS with no Critical lifecycle, isolation, credential-disclosure, fallback, or document-consistency finding.
- Gemini returned FAIL only because it rendered Traditional Chinese comments as mojibake and concluded the files were not valid UTF-8.
- All 18 Production/Test/Config/Script files in the review scope, including the 12 files explicitly named by Gemini, were checked locally with a strict UTF-8 decoder plus BOM, CRLF, final-CRLF, and common mojibake-pattern scans. Result: `SCOPED_ENCODING_OK` and `MOJIBAKE_PATTERN_MATCHES=0`.
- Therefore the Gemini Critical is an external reviewer decoding false positive, not a repository encoding defect. No source rewrite was performed for a file that was already byte-correct.
- Both reviews preserve the real Development `WorkloadBindings` index-merge Warning already documented in SPEC and the explanation guide.

### Legacy Session-cache ownership investigation

Claude reported an Info observation that other `InMemoryDataContextSmallGroup` cache properties still use non-atomic `Get`-then-`Set` and ineffective eviction-state callbacks. Root-cause tracing established:

- The named legacy manager types do not implement `IDisposable`.
- Managers that access CRM receive the same process-wide `ToolUtilityFactory` singleton, rather than allocating one independent disposable CRM graph per Session entry.
- Concurrent first access can still create duplicate short-lived wrapper/data objects and overwrite state, so the pattern remains correctness/performance debt bounded by the 30-minute cache policy.
- Eviction must not be changed to dispose `subValue` indiscriminately. Doing so could dispose the process-wide ToolUtility graph from one Session and cause cross-Session use-after-dispose.
- The real pre-existing lifecycle blocker is that `ToolUtilityFactory` exposes only an internal test reset and no proven Production host-shutdown owner for its disposable CRM/trace graph. Final Phase 6 readiness must either remove that singleton behind Gateway or add exactly one deterministic process-lifetime cleanup owner.

This finding is not introduced by the Donation coordinator slice and does not invalidate its lease/drain state machine, but it prevents the overall legacy SDK/Data8 lifecycle from being declared complete.

### Additional artifact hygiene

The new Claude artifacts included a provider Session marker, and generated reviewer prompts/stderr copied the local Windows workload identity from configuration. Those generated-only values were removed without changing reviewer findings. Post-cleanup scans returned:

```text
RUN_LOCAL_IDENTITY_MATCHES=0
RUN_SENSITIVE_ASSIGNMENT_MATCHES=0
```

### Final status for this increment

- Donation Session lifecycle implemented scope: no credible Session/Memory/Resource Leakage finding remains.
- Development Local Gateway/config/browser/retired-probe scope: no Critical remains; one explicit workload-binding hardening Warning remains open.
- Overall Phase 4 remains in progress for real CE, OData projection, capacity, fault/soak/performance, and resource baseline.
- Phase 5 remains not enabled; `Package01FeeReadsEnabled=false`.
- Phase 6 remains report-only and additionally owns the legacy ToolUtility process-lifetime cleanup/removal blocker. Embedded, Data8, and `PowerPlatform.Dataverse.Client` remain retained.

Post-reconciliation review `20260730-030439-dynamics-gateway-documentation-reconciliation-final-review-reviewer` completed with full Gemini+Claude PASS (`ok=true`, no quota/degraded fallback). It confirmed these documents are suitable as the Phase 4～6 authority, retained the workload-binding Warning, and requested only the 18-file wording precision corrected above.

---

## 2026-07-30 Named workload binding set authorization-isolation review

### Implemented boundary

- Replaced the shared numeric `DynamicsGateway:WorkloadBindings` overlay with deployment-owned `ActiveWorkloadBindingSet` plus separate `WorkloadBindingSets:Central`, `Local`, and `Testing` subtrees.
- `ConfigurationGatewayOperationAuthorizer` enumerates direct set children, resolves exactly one active set, rejects blank/wildcard/unknown/scalar/childless selectors, and materializes only that set before publishing frozen SID/principal dictionaries.
- Development therefore cannot inherit a Central principal or Central-only operation even though base and Development configuration providers remain merged.
- Testing factories explicitly select an isolated nonempty `Testing` set instead of inheriting the base Central set.
- Request-time authorization remains synchronous, allocation-bounded, lock-free frozen lookup with no reload subscription, principal cache, timer, background Task, socket, connection, or new cleanup owner.

### TDD and runtime evidence

- RED first proved the old implementation returned `Succeeded=true` for the Central principal after loading real base plus Development JSON.
- GREEN returns `unmapped-principal` for that same Local-host attempt and fails startup for invalid/empty active sets.
- Targeted tests: Workload 23, request-body 24, Kestrel Negotiate 7, readiness 4; all passed.
- Fresh full Dynamics run: 235 passed, 0 failed, 1 ordinary LocalDB live-contract skip. The live contract had already passed separately.
- Fresh ChurchReport run: 367 passed, 0 failed.
- Fresh Release solution build: 0 warnings, 0 errors.
- Real Development Local Gateway repeated the 200/200/401/200/403/403/controlled-400 status matrix. Parent and child processes stopped; listener 7244 and temporary process logs returned to zero.
- Scoped `dotnet format --verify-no-changes`, strict UTF-8 without BOM, CRLF-only, final CRLF, mojibake scan, and `git diff --check` passed.

### External CCG result and limitation

- Gemini completed multiple attempts and consistently returned PASS with no Critical or Warning finding for authorization inheritance, selector fallback/path injection, Testing isolation, lifecycle/resource retention, comments, or encoding.
- Claude repeatedly exited at the provider CLI layer with status 1 and no usable review output. The self-healing runner classified this as `quotaBlocked=false`, `degradedFallback=false`; therefore Gemini PASS is not reported as a completed dual-model review.
- The final bounded retry is `20260730-040201-development-workload-binding-set-final-review-retry-reviewer` with `ok=false`, completed backend `gemini`, failed backend `claude`. A later full Gemini+Claude retry remains required before this increment can claim the project's mandatory external dual-model review gate.
- This external-review limitation does not reopen the locally reproduced authorization bug, but it keeps the CCG review phase in progress.

### Artifact and process hygiene

- Generated run artifacts were scrubbed of provider Session markers, local profile paths, configured identity/SID values, secret references, and other sensitive configuration values; post-cleanup scans returned zero matches.
- Temporary Claude command-shim files and their now-empty directories from interrupted runner attempts were removed; no recent shim directory remains.
- No Gateway, reviewer wrapper, Gemini, Claude, or related listener process remains active.

### Phase status

- The Development workload-binding index-merge Warning is closed by code, tests, runtime evidence, SPEC, and the Traditional Chinese explanation guide.
- Overall Phase 4 is still in progress for real CE 8.2/9.1, OData annotation projection, cross-process capacity, coordinator fault, soak/performance, and shutdown baselines.
- Phase 5 remains disabled with `Package01FeeReadsEnabled=false`.
- Phase 6 remains report-only; Embedded, Data8, and `PowerPlatform.Dataverse.Client` remain retained until their explicit replacement/removal gates pass.

---

## 2026-07-30 Valid-unmapped-SID authorization follow-up

### Verified Critical finding

An independent reviewer identified a real authorization defect in
`ConfigurationGatewayOperationAuthorizer.ResolveAuthenticatedBinding`:
an authenticated principal with a syntactically valid but unmapped Windows SID
could fall back to a configured binding with the same principal name. If an old
account name were reused by a new account with a different SID, the new security
authority could inherit the old workload's alias, operation, capacity, and audit
identity.

The executable contract is now:

- a present syntactically valid SID is authoritative;
- an unmapped valid SID returns `unmapped-principal` without name fallback;
- exact principal-name fallback is permitted only when the authenticated
  principal has no usable SID;
- rejection occurs before executor, admission, secret, token, or outbound
  transport work.

### TDD evidence

The previous success test was first changed to require HTTP 403, zero executor
calls, and no materialized execution request. Before the Production fix, the
test failed for the expected reason: actual HTTP status was 200. The minimal
Production change returns the SID lookup result immediately whenever a valid SID
is present.

The corrected SID case and the existing no-SID exact-name compatibility case
then both passed. The selector boundary suite was also expanded to cover a
missing selector, leading/trailing whitespace, `?`, `Local:0`, a true childless
JSON set, scalar-plus-children ambiguity, and case-insensitive exact selection.
The earlier test named “empty” was corrected because it had actually exercised
a scalar value rather than a real childless JSON object.

### Fresh local evidence

- `GatewayWorkloadBoundaryTests`: 31 passed, 0 failed.
- `SpeechMessage.Dynamics.Tests`: 243 passed, 0 failed, 1 ordinary opt-in live
  SQL skip.
- `ChurchReport.MemberInfo.Tests`: 367 passed, 0 failed.
- `SpeechMessageProducts.sln` Release build: 0 warnings, 0 errors.

All new or substantively modified Production/Test comments use detailed
Traditional Chinese and describe the trust boundary, fail-closed ordering,
owner/cleanup model, concurrency behavior, and performance/memory consequences.
The final documentation and task-artifact normalization was followed by the
strict UTF-8/no-BOM/CRLF/final-CRLF and full external dual-model gates recorded
below.

### Scope status

This closes the locally reproduced valid-SID/name-fallback defect only after
the remaining format/encoding and mandatory external review gates complete. It
does not complete overall Phase 4, enable `Package01FeeReadsEnabled`, remove
Embedded/Data8/`PowerPlatform.Dataverse.Client`, or close real CE 8.2/9.1,
OData projection, cross-process capacity, fault/soak/performance, Phase 5, or
Phase 6 gates.

### Mandatory dual-model review closure for this increment

The required self-healing runner completed the bounded authorization review:

```text
20260730-045814-valid-unmapped-sid-selector-final-review-reviewer
ok=true
degradedFallback=false
quotaBlocked=false
completedBackends=gemini,claude
```

- Gemini returned PASS with 0 Critical and 0 Warning findings.
- Claude returned PASS with 0 Critical and 0 Warning findings.
- Both reviewers accepted the authoritative-SID fail-closed rule, the no-SID exact-name compatibility path, the direct-child selector resolution, the expanded negative/positive selector tests, the request-time frozen lookup model, and the absence of new mutable caches, locks, timers, background tasks, sockets, or cleanup owners.
- This full result supersedes the earlier Claude-no-output limitation for this authorization-isolation increment. It closes only the mandatory external review gate for the valid-unmapped-SID and selector follow-up; it does not complete the overall Phase 4 program.

Generated review artifacts were sanitized without changing either model's
findings. The post-cleanup gate returned:

```text
SESSION_LEAKS=0
PROFILE_LEAKS=0
SID_LEAKS=0
CONFIG_VALUE_LEAKS=0
RECENT_SHIM_DIRECTORIES=0
LISTENER_7244=0
```

The project owner's documentation rule remains a release gate: every newly
added or substantively modified Production/Test/Tool/Script type, method, and
lifecycle member must have complete, in-depth Traditional Chinese comments
covering trust boundaries, unique ownership, concurrency, fail-closed order,
cancellation/timeouts, rollback/drain/dispose/cleanup, and performance/memory
trade-offs. All changed source, tests, tools, scripts, configuration, SPEC, and
documentation must be UTF-8 without BOM, CRLF-only, with a final CRLF.

### Fresh final local quality gate

```text
GatewayWorkloadBoundaryTests       31 passed / 0 failed / 0 skipped
SpeechMessage.Dynamics.Tests      243 passed / 0 failed / 1 opt-in live SQL skipped
ChurchReport.MemberInfo.Tests     367 passed / 0 failed / 0 skipped
SpeechMessageProducts.sln Release   0 warnings / 0 errors
Scoped dotnet format               35 C# files / passed
Traditional Chinese comment audit  36 program files / passed
Strict text encoding               60 delivery files / passed
git diff --check                   passed
```

The encoding gate used a strict UTF-8 decoder and rejected BOM, bare LF, bare
CR, missing final CRLF, and Unicode replacement characters. The comment audit
covered every changed or new `.cs` and `.ps1` file. The final sensitive and
resource scan remained at zero for provider Session markers, local profile
paths, configured identity/SID values, sensitive configuration values, recent
Claude shim directories, and the localhost 7244 listener.
