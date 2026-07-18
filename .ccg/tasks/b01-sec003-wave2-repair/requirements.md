# B01-SEC-003 Wave 2 Repair Requirements

## Authority

Execute only the approved contract in:

- `docs/project-modular-diagnostics/B01-identity-session-access-control/wave_2/plans.md`
- `docs/project-modular-diagnostics/B01-identity-session-access-control/wave_2/measurements.md`
- `docs/project-modular-diagnostics/B01-identity-session-access-control/wave_2/goals.md`

The existing Trellis `optimization-blueprint-workflow` task remains the global
Wave 2 tracker. Do not create another Trellis task and do not start B02.

## Evidence Gate

Before any product or test edit, verify all of the following with redacted,
durable evidence:

1. Non-production CRM row-version conditional update succeeds with the current
   row version and reports a distinguishable conflict for a stale row version.
2. The deployed ToolUtility/F03A owner confirms that no external binary caller
   of the three account APIs requires raw-password compatibility.
3. A resettable synthetic QA contact, least-privilege test identity, deployment
   target, and success/failure `ProcessLogin -> SetupSystemData` probe path are
   ready. The final route proof is produced after the repair candidate exists.

Evidence may contain only case IDs, owner role, artifact version, status,
key-or-raw classification, counts, and pass/fail. It must not contain account or
contact identifiers, credentials, hashes, salts, session values, response
bodies, or CRM payloads.

## Repair Outcome

If the evidence gate passes, implement only `B01-SEC-003` within the exact
allowlist in `plans.md`, test first, preserve route/response/authentication
contracts, and satisfy every measurement and goal. Claude is the only external
reviewer; Gemini is prohibited. A no-output Claude run must be recorded
truthfully and handled only by the currently permitted local fallback.

Commit only after final local verification, the non-production route probe,
review, and exact allowlist checks pass. Use a Traditional Chinese subject and
body. Update Wave 2 tracking and archive this CCG task without starting B02.
