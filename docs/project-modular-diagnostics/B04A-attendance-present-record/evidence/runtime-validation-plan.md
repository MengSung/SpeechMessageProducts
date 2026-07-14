# B04A Runtime Validation Plan

Final status: DEGRADED_REVIEW_PENDING

## Purpose

This is a validation plan only. No product code, tests, config, generated files, bin/obj/cache, lockfiles, or ledger entries are modified by this diagnostic pass.

## Security Validation

1. Route inventory:
   - Enumerate `InsertPresentRecord`, `UpdateSmallGroupPresentRecord`, and `DeletePresentRecord` effective route templates.
   - Verify authentication, authorization policy, anti-forgery, and middleware order applied to each route.
2. Ownership tests:
   - Build a test fixture with two users, two contacts, two lists, and two present records.
   - Attempt cross-list update/delete by passing another user's `key`.
   - Expected result: rejected before in-memory or CRM mutation.
3. Session freshness:
   - Run mutation after session expiration or login switch.
   - Expected result: rejected and no write to `InMemoryContext` or CRM.
4. Logging:
   - Execute update with sensitive `values`.
   - Expected result: logs contain correlation ID and operation metadata, not raw payload, session ID, phone, notes, or contact identifiers.
5. Create-on-read:
   - Query a contact with no present record.
   - Expected result after remediation: query returns empty/not found; explicit create command is required for write.

## Performance Validation

1. Baseline CRM call count:
   - Instrument create/update upload for 10, 50, and 200 members.
   - Count retrieve/create/update/assign operations and request latency.
2. Batch implementation comparison:
   - Repeat with batch prefetch/write wrapper.
   - Expected result: CRM read calls scale by entity type, not by member times helper calls.
3. Validation purity:
   - Run valid-member count on records with mixed active/inactive contacts.
   - Expected result after remediation: counting does not update contact entities.
4. Concurrency:
   - Run simultaneous updates to the same present record key.
   - Expected result after remediation: deterministic final state or explicit conflict response.

## Boundary Validation

1. Provider gate:
   - Add B04A tests for query, command, validation, and mapping services.
2. Consumer gate:
   - Add B04C scheduler/QR integration smoke proving B04C consumes the attendance contract without direct legacy partial access.
3. Static audit:
   - Scan for present-record mutation actions using `InMemoryContext` without a B04A mutation context.
   - Scan for `RetrieveEntity` / `UpdateEntity` inside loops in B04A-owned files.
   - Scan for name-based matching against present-record/contact display names.

## Rollback Strategy

- Introduce the new B04A services behind existing callers first.
- Keep legacy partial methods as adapters until provider and consumer gates pass.
- Roll back by switching adapters back to legacy methods; do not change CRM schema as part of the first remediation.
