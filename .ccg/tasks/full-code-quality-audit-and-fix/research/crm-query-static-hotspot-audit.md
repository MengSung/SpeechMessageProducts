# CRM Query And Static Hotspot Audit

Status: DONE_WITH_CONCERNS

Scope: read-only audit for Tasks 10 and 12 of `docs/superpowers/plans/2026-07-10-full-code-quality-audit-and-fix.md`.

Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree`

## Commands And Scans Run

- `git status --short`
- `rg --files ToolUtility/ConnectionOperations ToolUtility/ListOperations ToolUtility/EntityOperations SpeechMessageProducts.ChurchReport -g "*.cs"`
- `rg -n "new HttpClient|new LineMessagingClient|new RestClient|\.GetAwaiter\(\)\.GetResult\(\)|\.Wait\(|\.Result|Task\.Run|Thread\.Sleep" ... -g "*.cs"`
- `rg -n "new (System\.Net\.Http\.)?HttpClient|new (Line\.Messaging\.)?LineMessagingClient|new RestClient|new LineMessagingProcessorClass" ... -g "*.cs"`
- `rg -n "SessionId =|async override void|PostEvictionCallbacks|RegisterPostEvictionCallback" SpeechMessageProducts.ChurchReport -g "*.cs"`
- `rg -n "ColumnSet\(true\)|RetrieveMultiple|TopCount|PageInfo" ToolUtility/ConnectionOperations/CrmConnectionPool.cs ToolUtility/ListOperations ToolUtility/EntityOperations SpeechMessageProducts.ChurchReport -g "*.cs"`
- `rg -n "class .*Controller|HttpGet|HttpPost|Route|AllowAnonymous|Authorize|Session|_SessionUserId|User\.Identity|CanView" SpeechMessageProducts.ChurchReport/Controllers/ApiControllers -g "*.cs"`
- Dependency checks for `ToolUtility/Core/ToolUtilityFacade.cs`, `ToolUtility/QueryOperations/PresentRecordQueryService.cs`, `ToolUtility/QueryOperations/ComplexQueryService.cs`, `SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs`.

No `.ccg/spec/` directory exists in this worktree. Relevant `.trellis/spec/backend/*` and `.trellis/spec/guides/index.md` were read; the only directly relevant rule is avoiding sync-over-async in auth/session code.

## Files Found

- `ToolUtility/ConnectionOperations/CrmConnectionPool.cs`: synchronous CRM connection pool; remaining semaphore `Wait`.
- `ToolUtility/ListOperations/MarketingListService.cs`: marketing-list query and batch wrappers; remaining `Task.Run` and `ColumnSet(true)` matches.
- `ToolUtility/ListOperations/ListService.cs`: list/member query helpers; remaining `Task.Run`, `ColumnSet(true)`, and unbounded `RetrieveMultiple` matches.
- `ToolUtility/EntityOperations/EntityRepository.cs`: generic CRUD repository; default single-entity `ColumnSet(true)` fallback.
- `ToolUtility/EntityOperations/EntityQueryService.cs`: generic query service; several bounded fixes already present (`TopCount`, default `PageInfo`).
- `ToolUtility/EntityOperations/EntityOptimizedQueryService.cs`: optimized query service; bounded paths exist, fallback all-column modes remain for compatibility.
- `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs`: request action still blocks on async cache invalidation.
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs`: request-path LINE OAuth HTTP calls still construct `HttpClient` directly.
- `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs`: request-path `Task.Run` mutates controller/session-backed state.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs`: hot dedication key-in contact lookup uses `ColumnSet(true)` and has no `TopCount`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`: branch already fixes personal batch image authorization/cache-key leakage; one single-contact `ColumnSet(true)` remains after upload.
- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`: branch already adds bounded cache policy and eviction disposal.

## Dependencies

- `BaseChurchController.GetConnection()` -> `ICrmConnectionPool.AcquireConnection()` -> `CrmConnectionPool._semaphore.Wait(...)`.
- `HomeController.TestCachePerformance()` -> `ToolUtility.Caching.CrmCacheService.InvalidateAsync(...)`.
- `AuthenticationController.LineLoginOAuth.ExchangeCodeForToken()` / `GetLineUserProfile()` -> direct `new HttpClient()`.
- `SmallGroupController.HandleLineLogin()` -> `Task.Run(SetupViewBagForSmallGroup)` -> `ViewBag` and `InMemoryContext` mutation.
- `DonationPaymentProcessor.SaveKeyInDedication()` -> `GetContactForKeyIn()` -> `RetrieveMultiple(QueryByAttribute contact)` -> downstream `CreateFee`, `ResolveDedicationNotificationLineId`, and success message use a small contact column set.
- `ChurchListDataProcessor.QueryListByContactIdWithCache()` -> `ToolUtilityClass.QueryListByContactId()` -> `ToolUtilityFacade.QueryListByContactId()` -> `PresentRecordQueryService.QueryListByContactId()`; this hot dependency is outside the dispatch's narrow ToolUtility subfolder list but is on Task 10's broad scan path.

## Patterns

- Bounded cache/disposal pattern already applied: `InMemoryDataContextSmallGroup.ApplySessionCachePolicy` sets absolute/sliding expiration and eviction disposal at `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:211`.
- Personal image authorization/cache-key fix is already in branch for batch requests: `PersonalController.ImageUpload.cs:703` checks login contact and `PersonalController.ImageUpload.cs:729` rejects unauthorized requested contacts.
- Global MVC authorization is registered at `SpeechMessageProducts.ChurchReport/Startup.cs:389`; API controllers without `[Authorize]` are still covered unless marked `[AllowAnonymous]`.
- API controller scan: `SpiritLeaderLookupController` and `SchedulerDataController` have `[Authorize]`; `ShepherdMethodLookupController` returns static lookup metadata; `MultiGroupController` has no actions.
- LINE processor ownership appears fixed in branch: `LineMessagingProcessor/LineMessagingProcessorClass.cs:143` disposes internally owned clients; `Line.Messaging/LineMessagingClient.cs:123` marks token-only constructor obsolete.

## Findings

### Critical

None confirmed in this read-only pass.

### Warning

- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs:264`: confirmed hot CRM defect. `SaveKeyInDedication` logs `GetContact elapsed` at lines 215-218, then `GetContactForKeyIn` queries `contact` with `ColumnSet(true)` and no `TopCount`, even though it takes only `matches.Entities[0]`.
- `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:439`: confirmed sync-over-async request-path defect. `TestCachePerformance()` blocks on `InvalidateAsync(...).Wait()`; `CrmCacheService.InvalidateAsync` awaits distributed cache removal.
- `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:375` and `:419`: confirmed request-path socket-risk defect. `Startup.cs:164` already registers `services.AddHttpClient()`, but LINE token/profile requests still construct short-lived `HttpClient` directly.
- `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs:66`, `:71`, `:75`: confirmed request-path `Task.Run` misuse. The `SetupViewBagForSmallGroup()` branch mutates MVC `ViewBag` and `InMemoryContext` off the request thread (`SmallGroupController.ViewBag.cs:28`), and the other branches mutate session-backed in-memory state.
- `ToolUtility/QueryOperations/PresentRecordQueryService.cs:294` and `:305`: confirmed hot dependency outside the dispatch's narrow ToolUtility source list. `ChurchListDataProcessor` calls this six times per list setup path via cache miss; the query uses `ColumnSet(true)` and no `PageInfo`/`TopCount`.

### Info

- `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:106`: semaphore `Wait` is real blocking, but the public contract is synchronous and callers immediately use synchronous CRM SDK calls. Treat as owner/runtime decision unless the implementation phase is prepared to add async pool APIs and migrate controller call sites.
- `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:986`: `RegenerateSessionId()` blocks on `Session.CommitAsync()`, but `rg` found no call sites. It is still auth/session code and should not grow new callers in current form.
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:585`: admin/church-only resync endpoint constructs one `System.Net.Http.HttpClient` per request and gates parallel probes to 20. Prefer `IHttpClientFactory`, but this is less urgent than LINE OAuth.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:398`: single-contact post-upload `Retrieve(..., ColumnSet(true))` refreshes session contact. This is not a `RetrieveMultiple` hot endpoint; owner should decide which contact columns the session model requires before narrowing.

## Remaining Static Matches Classification

### Fixed In Branch

- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:211`: bounded expiration plus eviction disposal; static callback match is expected.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:550` and `:703`: personal image ownership checks and viewer-aware cache keys are present in the branch.
- `LineMessagingProcessor/LineMessagingProcessorClass.cs:143`: internally-created `LineMessagingClient` ownership is explicit and disposed.

### Allowed Compatibility Or False Positive

- `Line.Messaging/LineMessagingClient.cs:123` and `LineMessagingProcessor/LineMessagingClientFactory.cs:10`: compatibility constructor path, marked obsolete or factory-owned.
- `.Result` matches in `ListManagementController`, `FeeManagementController`, and `ListManagementDataManager` are model/view-message properties, not `Task.Result`.
- `Thread.Sleep` match in `SmallGroupReportController.cs:32` is commented out.
- `PostEvictionCallbacks` / `RegisterPostEvictionCallback` matches in cache services are cleanup/tracking callbacks, not defects.
- `Program.cs:234` background `Task.Run` starts GC monitoring, not a request-path sync-over-async pattern.

### Requires Owner Or Runtime Decision

- `CrmConnectionPool.cs:106`: changing `Wait` to `WaitAsync` requires an async pool contract and migration of `BaseChurchController.GetConnection()` consumers.
- `ToolUtility/ListOperations/*.cs` `Task.Run` batch wrappers: public library compatibility surface; product scan did not find direct ChurchReport callers beyond facade wrappers.
- `ToolUtility/EntityOperations/*.cs` generic `ColumnSet(true)` fallbacks: compatibility defaults for callers that do not specify columns; prefer gradual caller-level migration to named columns.
- `MemberInfoController.GetContactImagesBatch` and `PersonalController.GetContactImagesBatch`: CRM image queries are bounded by requested IDs and access checks, but no explicit max request count is enforced.
- `AuthenticationController.Private.cs:123` and `:130`: login/session contact hydration still uses `ColumnSet(true)` with `TopCount = 1`; owner should define the session contact column contract before narrowing.

### Confirmed Defects

- `DonationPaymentProcessor.FeeManagement.cs:264`: all-column, unbounded key-in contact lookup in a timed slow path.
- `HomeController.cs:439`: `.Wait()` on async cache invalidation in an MVC request action.
- `AuthenticationController.LineLoginOAuth.cs:375` and `:419`: direct `HttpClient` construction on LINE OAuth request path.
- `SmallGroupController.LineLogin.cs:66`, `:71`, `:75`: `Task.Run` over controller/session-backed mutations.
- `ToolUtility/QueryOperations/PresentRecordQueryService.cs:294`: hot dependency uses all columns and no paging/bound for list lookup.

## Suggested Minimal Fixes

1. `DonationPaymentProcessor.FeeManagement.cs:264`
   - Replace `new ColumnSet(true)` with named columns: `fullname`, `pager`, `new_personal_id`, `new_lineid`, `new_lineid_backup`, `parentcustomerid`, `ownerid`.
   - Set `TopCount = 1` on the `QueryByAttribute`.

2. `HomeController.cs:402` / `:439`
   - Change `TestCachePerformance()` to `async Task<IActionResult>`.
   - Replace `.Wait()` with `await cacheService.InvalidateAsync(...)`.

3. `AuthenticationController.LineLoginOAuth.cs:375` and `:419`
   - Use the registered `IHttpClientFactory` (`HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>()` or constructor injection).
   - Keep the existing async `PostAsync` / `GetAsync` flow.

4. `SmallGroupController.LineLogin.cs:66-79`
   - Remove `Task.Run` around `SetupViewBagForSmallGroup()` and session/in-memory model mutations.
   - Execute controller/ViewBag setup on the request thread; only move CRM calls behind real async abstractions if available.

5. `ToolUtility/QueryOperations/PresentRecordQueryService.cs:294` (dependency observation)
   - Replace all-column list query with the columns consumed by `ChurchListDataProcessor`: `listname`, `new_app_named`, `new_happy_start_date`, `new_happy_end_date`, `new_contact_list_arealeader`, `new_contact_race_leager_list`, `new_contact_family_leader_list`.
   - Add `PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }` or a product-approved `TopCount`.

## Risks

- Narrowing `ColumnSet(true)` on session/login contact hydration can break downstream code that expects arbitrary attributes on cached `Entity` objects; do it only with a defined column contract.
- Making the CRM connection pool async is not a one-line fix because the CRM SDK calls remain synchronous and many controller paths use `GetConnection()` synchronously.
- The current working tree has an existing modification in `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`; treat it as user/branch-owned.
