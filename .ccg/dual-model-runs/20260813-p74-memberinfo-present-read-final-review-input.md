# P7.4 ORG-CALL-00026 final local-only code review

Review the current uncommitted diff for the independently gated, local-only migration of
`memberinfo.present.retrieve.by.contact` / `ORG-CALL-00026`.

Required security/correctness properties:
- checked-in base and sub-gates remain false; no CE, traffic, P7.5, P8, push, or PR operation;
- false gate retains legacy ToolUtility route; true path is server-authorized before contact dispatch;
- no browser-owned profile/workload/owner/endpoint/connector authority;
- true path must be DTO-only: no ToolUtility, Entity, QueryExpression, IOrganizationService, fallback, retry, or swallowed cancellation;
- Data8 query must be fixed, CE 9.1, one page only, bounded, and fail closed on MoreRecords/schema/type/duplicate/byte limit errors;
- all response collections must be defensive request-local snapshots; no cross-user/profile or resource leakage;
- contact FullName must keep legacy row semantics via same fixed query, not a second CRM Retrieve.

Run only static/code review. Report Critical, Warning, Info with concrete file/line evidence. Do not suggest CE operations, enabling a gate, traffic switches, P7.5 removal, or P8 work.
