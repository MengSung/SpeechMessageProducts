# P7.3 Special-resource local implementation review

Review the current uncommitted P7.3 changes in this repository. Do not modify files.

Scope:
- `memberinfo.contact.retrieve.image`
- `memberinfo.contact.update.image`
- `newperson.contact.update.image`
- `metadata.optionset.retrieve.by.attribute`
- `stats.meeting.retrieve.by.sunday`

Important constraints:
- This task is local-only: no CE mutation/evidence, feature flag, traffic switch, Official Worker, P7.4/P7.5/P8 work, push, or PR.
- P7.2 historical Slice C remains closed; do not recommend retrying it.
- Validate strict profile/generation isolation, no CRM SDK/raw stream/cookie/entity crossing boundaries, immutable defensive copies, bounded cache/paging/input, cancellation/fault eviction, exact resource disposal, and fail-closed response contracts.
- Treat unsuccessful connector result as session state not safe for reuse, requiring fault eviction.
- Check matrix/registry/schema consistency and verify no consumer migration or ToolUtility-removal claim is made.

Review the diff and relevant P7.3 source/tests. Return ONLY Critical, Warning, Info findings with file/line and concrete justification. Do not request secrets or make external calls.