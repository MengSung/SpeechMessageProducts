# P7.4 ORG-CALL-00026 local-only architecture analysis

Review the planned repository-local migration for `ORG-CALL-00026`,
`memberinfo.present.retrieve.by.contact`.

## Current source

`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`, action
`LoadContactPresentRecords`, currently:

1. calls `EnsureCorrectUserData()`;
2. parses the browser `contactId` and calls `CanViewContact(contactGuid)`;
3. retrieves `contact.fullname` through legacy ToolUtility/CRM SDK;
4. executes a fixed `new_present_record` query filtered by
   `new_contact_new_present_record`, projected fields
   `new_present_recordid`, `new_sunday_present_this_week`,
   `new_group_present_this_week`, `new_explanation`, `new_sunday_date`, sorted
   `new_sunday_date desc`;
5. maps to `ContactPresentRecordRow` and returns DevExtreme data.

## Required result

Design a new disabled-by-default P7.4 typed DTO-only route for this *one*
capability, while the checked-in gate remains false and the legacy route remains
the compatibility path. The enabled branch must be server-authorized before
browser locator parsing and typed dispatch; must use only a deployment-owned
profile/workload; must use request-local immutable DTOs; and must have no raw
CRM SDK object, cache, fallback, retry, CE request, feature enablement, traffic
switch, P7.5 removal or P8 work.

The operation does not yet exist in the registry/Data8/ProductClient. Identify
the minimal safe fixed contract, query/cardinality/paging bounds, input and
response validation, cancellation/error behavior, exact places to change, and
the TDD tests needed. Pay particular attention to: no caller profile/owner/
endpoint/connector authority; A/B user/profile isolation; contact fullname;
UTC/legacy display semantics; `DataSourceLoader` compatibility; and avoiding
partial results.

Historical P7.2 Slice C is permanently closed and must not be retried. This is
local-only planning: do not suggest CE, fixture, live traffic, or mutation.

Output Critical/Warning/Info findings and a concise safe design recommendation.
