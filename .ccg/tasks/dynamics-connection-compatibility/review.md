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
