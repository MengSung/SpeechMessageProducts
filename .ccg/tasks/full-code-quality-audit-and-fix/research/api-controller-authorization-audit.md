# API Controller Authorization Audit

Status: DONE_WITH_CONCERNS

## Controllers inspected

- `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs`
  - `GET /SchedulerData/Get`: returns scheduler appointments through `DataSourceLoader`.
  - `POST /SchedulerData/Post`: adds an appointment to the controller data context.
  - `PUT /SchedulerData/Put`: updates an appointment by key in the controller data context.
  - `DELETE /SchedulerData/Delete`: deletes an appointment by key in the controller data context.
  - Current working tree state: controller has `[Authorize]` at line 30.
- `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs`
  - `GET /api/SpiritLeaderLookup/Get?id={listEntityId}`: returns spirit leader names for a caller-supplied CRM list id.
  - Current working tree state: controller has `[Authorize]` at line 28.
- `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/ShepherdMethodLookupController.cs`
  - `GET /ShepherdMethodLookup/Get`: returns static shepherd method lookup metadata.
  - `GET /ShepherdMethodLookup/GetType`: returns the same static lookup metadata as JSON content.
  - Current working tree state: no `[Authorize]`; treated as public metadata.
- `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/AssignSmallGroupController.cs`
  - Defines `MultiGroupController`; no public action endpoints found.

## Critical findings

### Spirit leader lookup lacks object-level list access check

- File: `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs:32`
- Why confirmed: the endpoint accepts a caller-supplied `id`, passes it to `SetupSpiritLeaderList`, and returns `SpiritLeader.Name` values. The helper uses that id directly at `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs:47` and `:51`.
- Data source: `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs:545` retrieves the CRM `list` by the supplied GUID, then iterates list members and returns contact full names at `:566` and `:568`.
- Auth state: current working tree requires authentication through `[Authorize]` at `SpiritLeaderLookupController.cs:28`, but there is no object-level check that the authenticated user's `ListManager` can access the requested list id.
- Minimal fix: keep `[Authorize]`, then reject ids that are not in the current user's loaded `InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData` or equal to the current `ActiveListId`. Return `Forbid()` for an authenticated user requesting a list outside that scope. Existing patterns to mirror are the membership scoping in `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:1018` and the list-id based flow in `SpeechMessageProducts.ChurchReport/Models/ListManager.cs:220`.

## Warning findings

- None remaining in the current working tree for anonymous access to the private API controllers inspected. `SchedulerDataController` and `SpiritLeaderLookupController` both currently have `[Authorize]`.

## Info findings

### Global authorization is disabled by configuration

- Files: `SpeechMessageProducts.ChurchReport/Startup.cs:377` registers MVC filters, including `GlobalAuthorizationFilter` at `:389`; `SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:25` skips enforcement when `Security:EnforceGlobalAuthorization` is false.
- Runtime config found: `SpeechMessageProducts.ChurchReport/appsettings.json:69` sets `"Security"`, with `"EnforceGlobalAuthorization": false` at `:70`.
- Impact for this audit: controllers without explicit `[Authorize]` are public in this configuration. The sensitive controllers currently rely on explicit `[Authorize]`, which is the lower-blast-radius fix because turning on the global filter would require auditing and annotating login/public routes with `[AllowAnonymous]`.

## Endpoints intentionally left unchanged

- `ShepherdMethodLookupController.Get` and `GetType`: return `ShepherdMethodData.ShepherdMethodList`, a hard-coded lookup list defined at `SpeechMessageProducts.ChurchReport/Models/ShepherdMethods.cs:24`. No contact, member, group, schedule, assignment, or CRM object data is returned.
- `AssignSmallGroupController` / `MultiGroupController`: no public actions found in the file.
- `SchedulerDataController`: current working tree already adds `[Authorize]`. Its data context is session-keyed at `SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs:40` through `:53`, and no separate caller-supplied CRM object id is used by `Get`; no additional object-level defect was confirmed from this file.

## Dependencies

- `Startup.cs` registers MVC and the global authorization filter.
- `GlobalAuthorizationFilter` enforces auth only when config enables it; otherwise explicit `[Authorize]` is needed.
- `SessionValidationMiddleware` is not an authorization boundary: when `_SessionUserId` is absent it calls next middleware at `SpeechMessageProducts.ChurchReport/Middleware/SessionValidationMiddleware.cs:123` through `:128`.
- `SpiritLeaderLookupController` depends on `DownloadHappyGroup.GetSpiritLeaderListString`, which reads CRM list/contact data.
- `SchedulerDataController` depends on `InMemoryAppointmentsDataContext`, which stores appointments under a key derived from the ASP.NET session id.

## Patterns

- Explicit controller authorization exists in `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:47`.
- Contact/member object access uses local helper checks before returning data in `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:1018`.
- Batch/object scoping avoids trusting caller-supplied ids by intersecting with allowed in-memory/CRM records in `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:1047`.
- List-scope data loading uses `ListManager.SetupIntegrateData` to find the requested list id in the current user's `m_MultiGroupList` before loading details at `SpeechMessageProducts.ChurchReport/Models/ListManager.cs:220`.

## Risks and assumptions

- The worktree changed during audit: `SchedulerDataController` and `SpiritLeaderLookupController` now contain `[Authorize]`, and an untracked `ApiControllerAuthorizationTests.cs` exists. Findings are based on the current working tree, not the initial pre-change read.
- Environment variables or deployment-specific configuration could override `Security:EnforceGlobalAuthorization`; no appsettings production/development override was found in the repository scan. The SpiritLeader object-level issue remains even if global auth is enabled.
- I did not run tests; this was a read-only source audit.
