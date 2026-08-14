# P7.4 MemberInfo server-owned assignment evidence review

Review the current uncommitted P7.4 child implementation for:

- fixed `memberinfo.authorization.assignment.resolve.by.subject` operation;
- Data8 fixed read / bounded 512-list evidence path;
- typed ProductClient projection and DI;
- ChurchReport `MemberInfoServerAssignmentEvidenceSource` adapter;
- request-local A/B subject/profile isolation, cancellation, failure handling, and resource ownership;
- matrix/registry consistency.

The capability is **local-only**. It must not change a controller, feature gate, CE traffic/mutation, ToolUtility removal, P7.5, or P8.

Inspect the full working-tree diff and the untracked P7.4 source/test files. Report only verified Critical, Warning, and Info findings, with file/line evidence. Treat any session, cross-user, cross-profile, credential, CRM SDK boundary, mutable collection, retry/fallback, unbounded query, or resource-lifecycle risk as Critical. Do not recommend out-of-scope cutover work.
