# Wave 2 Goals: X05Q-SEC-001

CONTRACT_STATUS: WAVE_PLAN_APPROVED
SELECTED_ISSUES: X05Q-SEC-001

## Approval Record

- Claude-only self-healing review result: `CLAUDE_UNAVAILABLE`. Both Claude attempts returned `no-usable-output`; therefore there were no usable Claude findings. Artifact: `.ccg/dual-model-runs/20260715-115151-wave2-x05q-contract-reviewer/summary.json`.
- Exactly one permitted controller-dispatched read-only Codex fallback re-review approved this X05Q Wave 2 contract for exactly `X05Q-SEC-001`, with no unresolved Critical or Warning findings.
- This is document-contract approval only. It does not satisfy any local, staging, runtime, rollout, rollback, or deployment proof gate; all proof and `BLOCKED` conditions below remain in force until separately evidenced.

## Measurable Success Targets

1. 100% of unauthenticated session-only, missing-session-user, principal/session mismatch, principal/ListManager mismatch, expired-session, account-mode mismatch, LINE key mismatch, cache-owner mismatch, stale-cache-invalid, and CRM failure or partial-failure scenarios are rejected with the exact reason codes in `measurements.md`.
2. 0 rejected or unauthorized request may write, clear, remove, restore, or invalidate session, cache, ListManager, `_MemberInfoAccess`, or CRM-backed identity state.
3. 100% of matching authenticated account requests are allowed without unnecessary ListManager setup or CRM setup.
4. 100% of valid cache-hit requests use cache only when owner fingerprint, account mode, selected date, and expiry match the authenticated principal and compatibility inputs.
5. 100% of permitted account or LINE rehydration requests are bounded to at most one ListManager setup and at most one CRM-backed setup per request.
6. 100% of successful rehydration commits session/cache/ListManager state atomically after complete success and records owner/mode/expiry metadata.
7. 0 decisions may treat session/account/password/LINE/cache state as independent authorization. The authenticated server principal remains authoritative in every local, staging, and runtime proof.

## Required Unchanged Behavior

- Existing ChurchReport legacy routes remain available through the adapter; route templates and controller inheritance are unchanged.
- Existing auth claim names remain readable: `church:contactId`, `church:account`, `church:pwdkey`, and `church:loginType`.
- Existing session key names remain readable for compatibility: `_SessionUserId`, `_SessionCreatedAt`, `_LoginAccount`, `_LoginPassword`, and `_MemberInfoAccess`.
- Matching authenticated account and LINE flows preserve observable success or redirect behavior except for explicitly measured reject cases required by this contract.
- `X05Q-SEC-002`, every performance issue, and all other module issues remain untouched.

## Required Local Validation Result

The wave can be considered locally successful only when:

- every scenario in `measurements.md` passes with the required decision, HTTP outcome, reason, and counters;
- the focused `ChurchReport.MemberInfo.Tests` command in `plans.md` passes;
- `SpeechMessageProducts.ChurchReport.csproj` builds with `--no-restore`;
- redacted test output proves no raw identity, account, password key, LINE id, contact id, cookie, token, or CRM payload was emitted;
- Claude or the approved read-only Codex fallback approves the final three wave documents with no unresolved Critical or Warning findings.

## Deployment Blockers

Even after local tests pass, deployment must remain `BLOCKED` when any item below is true:

- the real session provider cannot reproduce missing, expired, cleared, and mismatched session state;
- CRM/ListManager rehydration success, timeout, missing record, exception, and partial payload cannot be proven in an isolated redacted environment;
- cache owner fingerprint, account mode, selected date, and expiry cannot be observed without raw identities;
- representative account, LINE, and anonymous legacy routes cannot be exercised;
- telemetry cannot prove zero unauthorized mutation;
- the enforcement flag or rollback switch is absent or untested;
- only mock tests exist and no staging/runtime proof has been captured.

## Rollback Conditions

The wave is unsuccessful and must roll back when any of these occur:

- any unauthorized scenario mutates session, cache, ListManager, `_MemberInfoAccess`, or CRM-backed state;
- any principal/session, principal/ListManager, LINE key, account-mode, or cache-owner mismatch is allowed;
- any CRM failure or partial rehydrate commits partial session/cache/ListManager state;
- any matching legacy route changes status or redirect shape without owner-approved compatibility exception;
- implementation requires changes outside the allowlist in `plans.md`;
- repair attempts broaden the scope to `X05Q-SEC-002`, a performance issue, a route facade rewrite, or another module.

Rollback scope is limited to the adapter, allowed `BaseChurchController` wiring, allowed cache metadata, and decision tests created for `X05Q-SEC-001`. Rollback must not rename auth claims, rename session keys, rewrite routes, change CRM schema, or remove legacy route compatibility.
