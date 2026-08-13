# P7.4 authorized fee contact read — architecture and security analysis

## Scope

Analyse a proposed local-only P7.4 consumer migration for exactly
`ORG-CALL-00005` (`fee.dedication.retrieve.by.contact`) in ChurchReport.
No code changes, CE request/mutation, feature enablement, traffic switch,
P7.5, or P8 action are authorized by this analysis.

## Current facts

- `Package01FeeReadsEnabled` remains false in all deployment settings.
- The typed `IPackage01FeeReadClient.RetrieveDedicationFeesByContactAsync`
  is implemented and has existing CE 9.1 read evidence, but its ChurchReport
  AJAX consumer is not migrated.
- `DedicationAuditController.GetFeesByContactId(string id)` currently accepts
  a browser-supplied contact GUID, then delegates to
  `DonationPaymentManager.GetDedicationFeesByContactIdAsync`.
- The current service retrieves that contact through ToolUtility and mutates
  a session-owned `DonationPaymentFormModel`; it then returns its fee list.
- Existing product policy identifies an accounting worker from the
  server-resolved login contact's `new_church_jobtitle`. The current browser
  contact ID must never become authority.

## Proposed shape

1. At the controller, rehydrate existing session data then fail closed unless
   the server-resolved current login contact has the established accounting
   role. The helper must not retrieve a browser-selected contact or use the
   browser ID as an identity/role source.
2. With the existing flag false, preserve the legacy query behavior after the
   same authorization boundary.
3. With the flag true, parse only a non-empty GUID locator and route it to
   the existing typed Package01 client. Do not retrieve a target CRM Entity,
   do not rehydrate a DTO into an Entity, and do not fall back to ToolUtility
   after a typed fault/cancellation.
4. The typed branch returns a fresh request-local fee result (rows and total)
   to the controller instead of mutating `DonationPaymentFormModel` or
   retaining any request/user/profile/DTO state in a singleton/static/cache.
5. Preserve cancellation; every semaphore/lease has a deterministic release
   path. Feature flags remain false.

## Required analysis output

Return a concise report with:

1. Critical security/correctness risks in this exact proposal.
2. Whether the accounting-role scope is an adequate server-side authorization
   boundary for this existing audit endpoint, and what fail-closed behavior is
   required when the server login-contact snapshot is unavailable.
3. Concrete invariant and test recommendations: false-gate compatibility,
   true-gate no-target-Entity/no-legacy-fallback, A/B isolation,
   cancellation, atomic result/no model mutation.
4. Explicitly identify anything that must remain outside this task.

Do not propose profile/endpoint/credential selection, a CE operation, a flag
change, traffic switch, or a broad ToolUtility rewrite.
