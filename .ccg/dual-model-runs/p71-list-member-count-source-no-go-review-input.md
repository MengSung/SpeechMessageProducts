# P7.1 ORG-CALL-00047 source-only no-go final review

Review only task-record changes for `.trellis/tasks/08-14-p71-list-member-count-typed-read/` and
`.ccg/tasks/p71-list-member-count-typed-read/`.

## Intended result

- The child records a local design no-go for direct migration of `list.members.count.by.listid`.
- It cites the legacy static `listmember` query, dynamic CRM `list.query` -> `FetchExpression` execution,
  mutable login/list workflow, and shared ToolUtility service fallback.
- It forbids static-only partial migration, caller-supplied listId as authority, raw CRM objects/queries,
  CE, feature gates, traffic, P7.5 and P8 changes.
- It lists the required future authorization/template/isolation/lifecycle conditions without implementing them.

## Required review

1. Find only actual Critical/Warning defects in the task records: source accuracy, authorization/isolation,
   scope control, accidental upgrade claims, or missing recovery condition.
2. Verify that no proposed change turns stored CRM FetchXML into Gateway executable input.
3. Do not recommend production code, CE operation, gate enablement, traffic change, P7.5 removal or P8.
4. Treat text encoding as a finding only when the raw file bytes prove invalid UTF-8/BOM/replacement/mixed line endings;
   the local byte-level check has already established UTF-8 without BOM, CRLF, final CRLF and no U+FFFD.

OUTPUT: Critical / Warning / Info findings with exact file references. State explicitly if no Critical/Warning finding exists.
