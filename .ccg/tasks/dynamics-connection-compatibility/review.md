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
