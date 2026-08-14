# P7.4 ORG-CALL-00033 source-only final review

Review only the task-owned documents and the parent task metadata changed for
the `ORG-CALL-00033` relation-goal source-only local design no-go.

## Requirements

- Do not permit a Church-only partial migration: every current consumer derives
  authorization through `GetAccess` / `CanViewContactsBatch`; Shepherd may use
  saved-credential `ListManager` loading.
- No Session, `InMemoryContext`, `ListManager`, ToolUtility, browser locator,
  caller profile/connector/credential/query, or old `allowedIds` may be
  described as a valid Gateway authorization authority.
- Confirm the no-go includes the unbounded `RetrieveAllEntities(connection)`
  paging and catch-all error-to-empty formatting issues.
- Confirm no production/runtime/CE/gate/traffic/matrix/P7.5/P8 change is
  presented as complete.
- Confirm recovery conditions require a new immutable server-derived MemberInfo
  authorization boundary before relation-goal registry/Data8/ProductClient work.

## Files under review

- `.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/`
- `.ccg/tasks/p74-memberinfo-relation-goal-read-boundary/`
- `.trellis/tasks/08-12-churchreport-productclient-cutover/task.json`

Return only concise Critical / Warning / Info findings. Do not request or
recommend CE actions, feature enablement, traffic change, fallback/retry,
P7.5 removal, or P8 deployment.
