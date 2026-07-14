# B04C Extraction Analysis

## Cohesive Domain Boundary

B04C has a clear domain cluster: QR scan verification, QR attendance/poll command execution, scheduler command validation, Sunday calculation, and weekly schedule settings. The current implementation spreads this across controllers, QR utility classes, PollManager, in-memory scheduler context, views, and Startup route mapping.

## Proposed Extractable Services

- `IQrScanVerifier`: validates signed QR token, expiry, nonce, action type, and server-verified LINE identity.
- `IQrAttendanceCommandService`: executes course, small-group, Sunday, personal, and poll QR commands against F03A CRM APIs.
- `ISchedulerCommandService`: validates and applies scheduler create/update/delete commands with B01 authorization and B04B ownership checks.
- `IB04CBoundaryAudit`: static/CI check that B04C routes, utilities, and views do not bypass verifier/authorization boundaries.

## Input And Output Contracts

- QR verifier input: signed token, request context, LIFF identity proof.
- QR verifier output: scan context containing QR type, target entity id, action, expiry, user identity, and replay verdict.
- Attendance command input: scan context plus command type.
- Attendance command output: user-facing result, CRM write summary, idempotency verdict.
- Scheduler command input: authenticated principal, appointment DTO/key, command type.
- Scheduler command output: authorized mutation result and validation errors.

## Dependency Direction

- B04C may call B01 identity contracts and F03A CRM contracts.
- B04C should not own LINE transport, small-group master data, attendance master data, or appointment/equipment workflows.
- B04A/B04B data concepts should enter B04C through explicit DTO/command contracts, not by scattered utility calls.

## Rollback Boundary

Introduce services beside existing utilities, then route one QR type at a time through the verifier/command service. Keep legacy utility methods callable until runtime validation gates are green.
