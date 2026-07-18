# Full Code Quality Audit And Fix Review

## API And Query Audit

- Controllers inspected:
  `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/*.cs`,
  `PersonalController.ImageUpload.cs`, `MemberInfoController.cs`, `HomeController.cs`,
  `SmallGroupController.LineLogin.cs`, `AuthenticationController.LineLoginOAuth.cs`.
- Authorization defects fixed:
  `SchedulerDataController` and `SpiritLeaderLookupController` now require authorization.
  `SpiritLeaderLookupController` also checks the requested list id against the active/loaded lists before returning leader names.
  `PersonalController.ImageUpload.cs` now enforces same-contact ownership before cache or CRM image access and uses viewer-aware cache keys.
- Query bounds or ColumnSet fixes:
  `DonationPaymentProcessor.FeeManagement.cs` key-in contact lookup no longer uses `ColumnSet(true)` and now uses `TopCount = 1`.
  `ToolUtility/QueryOperations/PresentRecordQueryService.cs` `QueryListByContactId` now uses a named list-column contract and `PageInfo` with `Count = 5000`.
- Sync/socket/thread hot spots fixed:
  `HomeController.TestCachePerformance` now awaits cache invalidation instead of calling `.Wait()`.
  `AuthenticationController.LineLoginOAuth.cs` uses `IHttpClientFactory` through request DI for LINE token/profile calls.
  `SmallGroupController.LineLogin.cs` no longer uses `Task.Run` for session/ViewBag/InMemoryContext mutations.
- Matches intentionally left unchanged:
  `ShepherdMethodLookupController` returns static lookup metadata.
  `AssignSmallGroupController` has no actions.
  `Security:EnforceGlobalAuthorization=false` remains a rollout decision until the anonymous whitelist/staging matrix is complete.

## Secret Rotation Required

Checked-in active-looking values were removed from `SpeechMessageProducts.ChurchReport/appsettings.json`.
Static configuration builders that previously only read `appsettings.json` now also call `.AddEnvironmentVariables()`, so deployment can provide replacements via hierarchical environment variables such as `LineMessaging__Jesus__ChannelAccessToken`.

Rotate any value that has ever existed in git history for these keys:

- `LineMessaging:*:ChannelAccessToken`
- `LineLogin:ChannelSecret`
- `MiniApp:ChannelSecret`
- `CrmConnection:Password`
- `LinePay:ChannelSecret`
- `Payment:Profiles:*:Credentials:*`
- `Sinopac:A1/A2/B1/B2/XKeyID`
- `Sandbox:A1/A2/B1/B2/XKeyID`
- `MyPay:Key`
- `TSPG:StoreKey/StoreIV`

Placeholders such as `YOUR_MYPAY_IV_HERE` remain non-secret deployment placeholders and still require real environment values before payment flows can run.

## Remaining Static Matches

### Fixed In This Branch

- `SessionValidationMiddleware.cs`: session invalidation now awaits `CommitAsync`.
- `SessionAttribute.cs`: legacy action filter no longer keeps per-request instance state or uses `async void`.
- `InMemoryDataContextSmallGroup.cs`: per-session cache entries now use bounded expiration and dispose evicted disposable values.
- `LineMessagingProcessorClass.cs`: internally created LINE clients are explicitly owned/disposed.
- `DonationPaymentManager.cs`: no longer inherits MVC `Controller`.
- `PersonalController.ImageUpload.cs`: personal contact image access is owner-scoped and viewer-aware.
- `HomeController.cs`: request action no longer blocks on async cache invalidation.
- `AuthenticationController.LineLoginOAuth.cs`: LINE OAuth no longer constructs request-path `HttpClient` instances directly.
- `SmallGroupController.LineLogin.cs`: request/session mutations are no longer executed inside `Task.Run`.
- `DonationPaymentProcessor.FeeManagement.cs`: key-in contact lookup is bounded and narrow.
- `PresentRecordQueryService.cs`: list lookup query is bounded and narrow.
- `appsettings.json`: active-looking checked-in secrets were blanked.

### Allowed Compatibility Or False Positive

- `*.Tests/*.cs`: tests construct `HttpClient` and `LineMessagingClient` around in-memory handlers.
- `Line.Messaging/LineMessagingClient.cs` and `LineMessagingProcessor/LineMessagingClientFactory.cs`: public SDK/factory compatibility paths.
- `.Result` matches in MVC/list manager files are model/view result properties, not `Task.Result`.
- `PostEvictionCallbacks` / `RegisterPostEvictionCallback` matches are cleanup/tracking callbacks.
- `Program.cs` background `Task.Run` starts debug GC monitoring, not request-path sync-over-async.

### Requires Owner Or Runtime Decision

- `ToolUtility/ConnectionOperations/CrmConnectionPool.cs`: `_semaphore.Wait(...)` is a synchronous pool contract; making it async requires migrating `BaseChurchController.GetConnection()` consumers and still accounting for synchronous CRM SDK calls.
- `ToolUtility/ListOperations/*.cs` and some `ToolUtility/QueryOperations/*.cs`: remaining `Task.Run` / `ColumnSet(true)` matches are library compatibility surfaces or generic fallback helpers. Caller-level migration to named column contracts is safer than changing all generic defaults in one branch.
- `BaseChurchController.RegenerateSessionId()`: still blocks on `Session.CommitAsync()`, but scan found no call sites. It should not gain new callers without async conversion.
- `AuthenticationController.Private.cs` and `PersonalController.ImageUpload.cs`: some session/contact hydration still uses `ColumnSet(true)` for single-entity refresh. Narrowing requires a full cached contact column contract.
- `MemberInfoController.GetContactImagesBatch` and `PersonalController.GetContactImagesBatch`: image queries are access-checked and requested-id bounded, but no explicit maximum request count is enforced yet.

## Verification Log

- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RequestPathHotspotScanTests"`: passed, 4 tests.
- `dotnet test ToolUtility.Tests\ToolUtility.Tests.csproj --filter "FullyQualifiedName~PresentRecordQueryServiceTests"`: currently blocked by pre-existing stale ToolUtility.Tests compile errors after enabling the project to restore against `net10.0`; a focused sub-agent is repairing the test API drift.
