# B04C Runtime Validation Plan

## Scope

Validate B04C QR and scheduler hypotheses without changing product code during this diagnostic phase.

## B04C-SEC-001 QR Identity And Replay Validation

- Build a request-level trace for QR view -> LIFF profile -> QR POST -> CRM write.
- Test tampered `UserLineId` in the AJAX payload while keeping a valid QR id.
- Test replaying the same QR POST after the original scan completes.
- Test two concurrent scans with different QR ids and users to detect context mixing.
- Expected verdict: keep if server accepts forged/replayed identity or QR target; rewrite if a global LIFF token verifier blocks it.

## B04C-SEC-002 Scheduler Mutation Validation

- Route-probe `SchedulerDataController.Get/Post/Put/Delete` as anonymous, authenticated wrong-owner, and authenticated valid-owner users.
- Verify anti-forgery behavior from the scheduler browser surface.
- Submit malformed `values` payloads and unknown keys.
- Expected verdict: keep if anonymous or wrong-owner mutation is possible; rewrite to validation/ownership-only if global authorization blocks anonymous access.

## B04C-PERF-001 QR CRM Call Count Validation

- Instrument F03A ToolUtility calls for retrieve/create/update per QR scan type.
- Capture p50/p95 latency with 1, 10, and 50 concurrent QR scans.
- Compare current per-record flow to a batch prototype after gate approval.

## Required Gates Before Optimization

- B04C route/auth smoke tests.
- B04C QR verifier unit tests.
- F03A batch CRM contract tests.
- B04A/B04B consumer integration tests where QR writes touch attendance or appointment concepts.
