# P7.1 dedication-booking typed-read architecture analysis

Review the planned local implementation for authoritative matrix ORG-CALL-00041.

Current legacy implementation: `ChurchReport.Services.DonationBookingService.FillBookingList` invokes
ToolUtility FetchXML by contact and then performs a per-row `RetrieveEntity` for `new_dedication_booking`.

Proposed scope:
- Add a bounded server-owned Data8/Package01 read capability
  `payments.dedication.retrieve.by.contact`.
- Add a dedicated closed wire response and ProductClient DTO/client; only `contactId` is a required typed input;
  `contactName` is optional compatibility data and cannot influence query scope.
- Fixed projection and bounded RetrieveMultiple; no Entity, EntityCollection, FetchXML, QueryBase, generic query,
  caller-supplied endpoint/profile/connector/credential, cache, retry, consumer migration, feature enablement, CE calls,
  fixture, P7.5, or P8.
- ChurchReport consumer remains unchanged in this child. A later P7.4 task owns server authorization, disabled gate,
  rollback and consumer cutover.

Review for: exact data-flow risks, contract naming/placement, response-boundary/isolation defects, Data8 lease lifecycle,
bounded projection/pagination, cross-profile A/B leakage, backwards compatibility, tests that must fail first, and whether
the proposed boundary might accidentally claim consumer or CE evidence.

Output: Critical / Warning / Info findings only, with exact files and concrete remediation. If no finding, say so.
