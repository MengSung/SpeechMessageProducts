# B03 Security Analysis

Status: LOCAL_DIAGNOSIS_COMPLETE_CCG_PENDING
Module: B03
Mode: DIAGNOSIS_ONLY

## Non-Finding

Do not report missing `[Authorize]` as a standalone B03 defect. The host registers
`GlobalAuthorizationFilter` in MVC at `SpeechMessageProducts.ChurchReport/Startup.cs:377`
through `Startup.cs:389`; the filter allows only anonymous routes, authenticated
users, or configured server-session identity fallback at
`SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:23`
through `GlobalAuthorizationFilter.cs:39`.

## Finding 1: SaveIntegrate Has A Weak Mutation Boundary

Severity: High

Evidence:

- `SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml:26` posts to
  `SmallGroup.SaveIntegrate`.
- `SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml:132` through
  `IntegrateView.cshtml:139` sends an AJAX POST with weekly report data and a 3
  second timeout.
- `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs:33`
  through `SmallGroupController.Save.cs:39` exposes the mutating `[HttpPost]`
  action without a local anti-forgery attribute.
- Repository search found no global `AutoValidateAntiforgeryToken`; only
  `DiagnosticsController` has `[ValidateAntiForgeryToken]`.
- `SmallGroupController.Save.cs:65` through `SmallGroupController.Save.cs:71`
  captures selected date, account, password, login type, weekly-report object,
  member data, and active list ID from session-scoped `InMemoryContext.ListManager`.
- `SmallGroupController.Save.cs:84` through `SmallGroupController.Save.cs:111`
  starts untracked `Task.Run` and calls upload with `CancellationToken.None`.
- `SmallGroupController.Save.cs:123` through `SmallGroupController.Save.cs:166`
  mutates captured member lists and swallows background failures into debug/trace.
- `SmallGroupController.Save.cs:168` through `SmallGroupController.Save.cs:169`
  returns success before CRM persistence completes.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.AsyncWrapper.cs:51`
  through `UploadIntegrateData.AsyncWrapper.cs:72` wraps synchronous upload work in
  another `Task.Run`.

Assessment:

This is not an authentication finding. It is a state-changing B03 flow that relies
on session-cached mutable state, lacks visible B03-local anti-forgery validation,
does not show a request-time user/list authorization guard, and reports success
before persistence completion.

Recommended next action:

Add anti-forgery validation and AJAX token transmission, re-check current
contact/list authorization immediately before persistence, and replace untracked
fire-and-forget upload with an idempotent queued command plus durable status.

## Finding 2: SpiritLeader Lookup Uses Caller-Supplied List IDs

Severity: Medium

Evidence:

- `SpeechMessageProducts.ChurchReport/Views/Home/DetailGrid.cshtml:79` through
  `DetailGrid.cshtml:84` calls `SpiritLeaderLookup.Get` with row data
  `HappyGroupListEntityId`.
- `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SpiritLeaderLookupController.cs:27`
  through `SpiritLeaderLookupController.cs:35` exposes the API and returns loaded
  lookup values.
- `SpiritLeaderLookupController.cs:39` through `SpiritLeaderLookupController.cs:49`
  passes the supplied list ID directly to `DownloadHappyGroup`.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs:541`
  through `DownloadHappyGroup.cs:577` retrieves the supplied list and its
  member/contact records, then returns qualified leader names.
- `DownloadHappyGroup.cs:2765` through `DownloadHappyGroup.cs:2797` chooses static
  or dynamic CRM list-member retrieval by the supplied `ListEntityId`.

Assessment:

The local B03 path does not show a check that the current contact/session may
view the supplied list before returning contact-derived names. CRM security may
mitigate this, so runtime exploitability is pending, but the module-level guard is
not visible.

Recommended next action:

Bind the lookup to current session active-list context or validate the supplied
list ID against the authenticated contact's permitted B03 list set before CRM
member/contact retrieval.

## Rejected Candidates

- Missing `[Authorize]`: rejected because global authorization exists.
- Any `Task.Run` is automatically Critical: rejected. The retained issue is the
  combined mutating CRM flow, session state, missing visible anti-forgery/list
  guard, and untracked completion.
- Small-group cache registration: rejected as security; no disclosure path was
  found.
