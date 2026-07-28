# Dynamics Phase 4 isolation hardening analysis

Analyze the smallest safe, test-driven code changes for the existing high-risk
task `.trellis/tasks/07-23-dynamics-connection-compatibility`. Do not modify
repository files or remote systems.

## Authorised scope

- Product code and VM configuration are authorised by the owner, but
  `Package01FeeReadsEnabled` must remain `false` until all live gates pass.
- No existing CRM relying-party trust may be replaced or destructively changed.
- No secret, token, password, browser cookie, user/LINE/session identifier, or
  raw authorization redirect URL may be printed, committed, or retained.

## Fresh local findings to verify against source

1. `WebApiServiceCollectionExtensions` adds the named `dynamics-adfs-token`
   client without a primary handler policy. `AdfsOAuthTokenProvider` uses that
   factory path, so it can inherit default cookies, redirects, proxy and
   decompression behavior. Its factory-created wrapper is not disposed.
2. The token provider reads an entire token endpoint body and embeds an error
   body preview into an exception.
3. `DynamicsHttpTransport` sets `PreAuthenticate = true`, even though the
   design requires it disabled by default.
4. `OrganizationAdmissionManager` uses `_inFlight.CurrentCount` plus a
   separate queued counter, rather than an atomic bounded reservation for
   in-flight plus queued work. A burst can retain more requests than the
   configured local limit.
5. `InMemoryRuntimeHostSlotCoordinator` makes count-then-set decisions without
   a local atomic critical section. It remains intentionally non-durable and
   is not a production multi-host solution.

## Required outcome

Give a Traditional Chinese Critical/Warning/Info report and a PASS/FAIL verdict
for these narrow local hardening changes. Address exact invariants, minimal
source/test files, risks of false fixes, TDD cases, disposal/cancellation rules,
and which Phase 4 / production blockers would still remain after the changes.
Do not recommend implementing `client_credentials` until live ADFS/CRM proof is
obtained.
