# Wave 2 Measurements: X05Q-SEC-001

CONTRACT_STATUS: WAVE_PLAN_APPROVED
SELECTED_ISSUES: X05Q-SEC-001

## Approval Record

- Claude-only self-healing review result: `CLAUDE_UNAVAILABLE`. Both Claude attempts returned `no-usable-output`; therefore there were no usable Claude findings. Artifact: `.ccg/dual-model-runs/20260715-115151-wave2-x05q-contract-reviewer/summary.json`.
- Exactly one permitted controller-dispatched read-only Codex fallback re-review approved this X05Q Wave 2 contract for exactly `X05Q-SEC-001`, with no unresolved Critical or Warning findings.
- This is document-contract approval only. It does not satisfy any local, staging, runtime, rollout, rollback, or deployment proof gate; all proof and `BLOCKED` conditions below remain in force until separately evidenced.

## Measurement Rules

All identities, accounts, contact ids, LINE ids, cookie values, session ids, password keys, CRM payloads, and cache keys must be synthetic labels or redacted hashes. Permitted labels include `principal-A`, `principal-B`, `owner-A`, `owner-B`, `account-A`, `line-key-A`, and `cache-A`. Raw PII or secrets must not be written to test output, Markdown evidence, logs, or review prompts.

Every scenario must capture these fields:

- `decision`: `Allow`, `Reject`, `Rehydrate`, or `CacheHit`;
- `http_outcome`: fixed `200`, `401`, or `302:/Authentication/Login` unless the legacy route under test already has a more specific existing redirect shape;
- `reason`: one of the exact reason codes in the scenario matrix;
- `allow_count`;
- `reject_count`;
- `rehydrate_count`;
- `cache_read_count`;
- `cache_hit_count`;
- `cache_write_count`;
- `cache_mutation_count`;
- `session_read_count`;
- `session_write_count`;
- `session_clear_count`;
- `listmanager_setup_count`;
- `crm_setup_count`;
- `crm_mutation_count`.

Unauthorized, mismatching, stale-invalid, expired-session, and partial-failure scenarios must prove every mutation counter is `0`.

## Synthetic Scenario Matrix

| Case | Fixture | Expected decision | HTTP outcome | Reason | Required counters |
|---|---|---:|---:|---|---|
| claim-only-principal-route | authenticated `principal-A`; no session; no cache; route does not require legacy ListManager context | `Allow` | `200` | `principal_only_allowed` | `allow_count=1`, `reject_count=0`, `rehydrate_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0`, all mutation counters `0` |
| claim-only-legacy-context-required | authenticated `principal-A`; no session; no cache; route requires legacy ListManager context | `Reject` | `302:/Authentication/Login` | `missing_compatibility_context` | `reject_count=1`, `allow_count=0`, `rehydrate_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0`, all mutation counters `0` |
| session-only | unauthenticated request; session contains `owner-A` fields | `Reject` | `401` for AJAX, `302:/Authentication/Login` for browser navigation | `unauthenticated_principal` | `reject_count=1`, `allow_count=0`, `rehydrate_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0`, all mutation counters `0` |
| matching-account | authenticated `principal-A`; session account/password/login mode match; current ListManager owner/account/password match | `Allow` | `200` | `identity_match` | `allow_count=1`, `rehydrate_count=0`, `cache_write_count=0`, `cache_mutation_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0` |
| session-principal-mismatch | authenticated `principal-A`; session owner/account/password belongs to `owner-B` | `Reject` | `302:/Authentication/Login` | `session_principal_mismatch` | `reject_count=1`, `listmanager_setup_count=0`, `crm_setup_count=0`, `session_write_count=0`, `session_clear_count=0`, `cache_mutation_count=0` |
| listmanager-principal-mismatch | authenticated `principal-A`; current ListManager owner/account/password belongs to `owner-B` | `Reject` | `302:/Authentication/Login` | `listmanager_principal_mismatch` | `reject_count=1`, `listmanager_setup_count=0`, `crm_setup_count=0`, `session_write_count=0`, `session_clear_count=0`, `cache_mutation_count=0` |
| missing-session-user | authenticated `principal-A`; `_SessionUserId` missing where session validation is required | `Reject` | `302:/Authentication/Login` | `missing_session_user` | `reject_count=1`, all setup and mutation counters `0` |
| expired-session | authenticated `principal-A`; `_SessionCreatedAt` older than the configured lifetime currently enforced by `ValidateSession()` | `Reject` | `302:/Authentication/Login` | `expired_session` | `reject_count=1`, `rehydrate_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0`, `session_write_count=0`, `session_clear_count=0`, `cache_mutation_count=0` |
| stale-cache-valid-live-inputs | authenticated `principal-A`; session and ListManager compatibility inputs match; cache metadata for `owner-A` is stale or expired | `Rehydrate` | `200` after complete success | `cache_stale_rehydrate` | `rehydrate_count=1`, `cache_read_count=1`, `cache_hit_count=0`, `listmanager_setup_count<=1`, `crm_setup_count<=1`, `cache_write_count=1` only after full success, no partial commit |
| stale-cache-invalid-live-inputs | authenticated `principal-A`; cache is stale or expired; live session/ListManager inputs mismatch or are missing | `Reject` | `302:/Authentication/Login` | `cache_stale_no_valid_live_identity` | `reject_count=1`, `rehydrate_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0`, all mutation counters `0` |
| account-mode-mismatch | authenticated `principal-A`; principal/session modes differ between `ACCOUNT` and `LINE` | `Reject` | `302:/Authentication/Login` | `account_mode_mismatch` | `reject_count=1`, `rehydrate_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0`, all mutation counters `0` |
| line-matching-rehydrate | authenticated `principal-A`; `LoginTypeClaim=LINE`; account is `LineIdLogin`; password key and owner metadata match; cache miss | `Rehydrate` | `200` after complete success | `line_rehydrate_allowed` | `rehydrate_count=1`, `listmanager_setup_count<=1`, `crm_setup_count<=1`, `session_write_count>0` and `cache_write_count<=1` only after full success |
| line-key-mismatch | authenticated `principal-A`; LINE mode but session/ListManager/cache password key belongs to `owner-B` or differs from principal | `Reject` | `302:/Authentication/Login` | `line_key_mismatch` | `reject_count=1`, `rehydrate_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0`, all mutation counters `0` |
| cache-owner-mismatch | authenticated `principal-A`; cache candidate has owner fingerprint `owner-B` | `Reject` | `302:/Authentication/Login` | `cache_owner_mismatch` | `reject_count=1`, `cache_read_count=1`, `cache_hit_count=0`, `cache_write_count=0`, `cache_mutation_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0` |
| cache-hit-valid | authenticated `principal-A`; cache owner/account mode/selected date/expiry match; no live mismatch | `CacheHit` | `200` | `cache_hit_valid` | `cache_read_count=1`, `cache_hit_count=1`, `rehydrate_count=0`, `listmanager_setup_count=0`, `crm_setup_count=0`, `cache_write_count=0` |
| crm-rehydrate-success | valid authenticated `principal-A` plus matching compatibility inputs; CRM/ListManager setup returns complete payload | `Rehydrate` | `200` | `crm_rehydrate_success` | `rehydrate_count=1`, `listmanager_setup_count=1`, `crm_setup_count=1`, atomic session/cache commit after success |
| crm-rehydrate-failure | valid authenticated `principal-A` plus matching compatibility inputs; CRM timeout, missing record, exception, or partial payload | `Reject` | `302:/Authentication/Login` | `crm_rehydrate_failed` | `reject_count=1`, `listmanager_setup_count<=1`, `crm_setup_count<=1`, `session_write_count=0`, `cache_write_count=0`, `cache_mutation_count=0`, no downstream action |

## Baseline Procedure

Before product edits, the repair owner must run the focused baseline commands from `plans.md` and record:

- which scenario tests do not yet exist;
- current source references to `EnsureCorrectUserData`, `ValidateSession`, `RegenerateSessionId`, `IssueAuthTicketAsync`, `SetupListManager`, `_LoginAccount`, `_LoginPassword`, `_SessionUserId`, and `_MemberInfoAccess`;
- whether the current implementation can prove zero mutation on reject. If it cannot, record `UNPROVEN_BASELINE` rather than inventing a passing result.

## Local Proof

The local proof must use deterministic fakes:

- fake authenticated principals built from `LoginClaimsFactory`;
- fake session store with read/write/clear counters and controllable `_SessionCreatedAt`;
- fake cache service with owner metadata, expiry, and read/hit/write/remove counters;
- fake ListManager hydration boundary with setup counters;
- fake CRM/ListManager rehydration result that can return success, timeout, missing record, exception, and partial payload.

Local success requires every row in the matrix to pass with the exact decision, HTTP outcome, reason, and counter constraints. Mock-only local success is sufficient for the local contract proof but not for deployment approval.

## Staging and Runtime Proof

Deployment remains blocked until an owner-authorized staging/runtime run proves the same matrix against:

- the real configured session provider, including missing, expired, cleared, and mismatched session state;
- an isolated redacted CRM tenant or approved deterministic CRM harness for success, timeout, missing record, exception, and partial payload;
- cache ownership and expiry metadata observable without raw identities;
- representative legacy routes inheriting `BaseChurchController`, covering authenticated account, LINE, and anonymous requests;
- feature-flag rollback and telemetry for decision/reason/counter output.

Capture location for future staging/runtime evidence must be explicitly authorized by the owner before writing. This planning subagent is not authorized to create evidence directories.

## No-Regression Observation

Matching authenticated legacy requests must preserve existing route availability, status code or redirect shape, and ListManager-visible behavior. Rejected requests must not reach business actions, shared cache mutation, session mutation, ListManager setup, or CRM setup. Any deviation must be recorded as a failed goal or an explicitly owner-approved compatibility exception before repair approval.
