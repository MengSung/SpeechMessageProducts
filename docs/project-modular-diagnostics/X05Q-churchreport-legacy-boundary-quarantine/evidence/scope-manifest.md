# X05Q Scope Manifest

Module: X05Q
Workspace: X05Q-churchreport-legacy-boundary-quarantine
Mode: DIAGNOSIS_ONLY
Map source: docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md
Nested agent count: 0

## Boundary Contract

X05Q owns ChurchReport legacy boundary quarantine material: files that are mixed, not safely attributable to one business module, or catch-all `SpeechMessageProducts.ChurchReport/**` files not matched by other B/X owners. The map states X05Q is not a module that can be optimized as one unit; it may only perform responsibility proof, caller/data-flow proof, extraction, reclassification, or approved retirement.

## Explicit X05Q Owners From Map

- `Controllers/BaseChurchController.cs`
- `Controllers/HomeController.cs`
- `Domain/Constants/CommitmentConstants.cs`
- `Extensions/ListManagerCacheExtensions.cs`
- `Services/Navigation/**`
- `wwwroot/js/TreeView.js` because no view reference was found in the map review.
- All unmatched version-controlled `SpeechMessageProducts.ChurchReport/**` files after higher-priority owners are applied.
- Other `ChurchReport.MemberInfo.Tests/**/*.cs` when no tested owner can be identified.

## Read Evidence

- `Controllers/BaseChurchController.cs:60` defines the shared base controller for many ChurchReport controllers.
- `Controllers/BaseChurchController.cs:130` stores `IHttpContextAccessor`; `:190-207` exposes a new `HttpContext` accessor that falls back to `base.HttpContext`.
- `Controllers/BaseChurchController.cs:558-592` caches `_MemberInfoAccess` in ASP.NET Session and can derive access from `InMemoryContext`.
- `Controllers/BaseChurchController.cs:641-764` reconciles ASP.NET Session, password, account, LINE claim, and `InMemoryContext.ListManager`.
- `Controllers/BaseChurchController.cs:902-950` validates `_SessionUserId`, `_SessionCreatedAt`, and `InMemoryContext.ListManager.m_Account`.
- `Controllers/BaseChurchController.cs:978-998` clears and rehydrates session fields, but logs that ASP.NET Core does not rotate the Session ID.
- `Controllers/HomeController.cs:65-390` contains backward-compatible `/Home/*` routes forwarding to multiple business controllers.
- `Controllers/HomeController.cs:80-88`, `:197-202`, `:218-222`, `:237-245`, `:659-666`, and `:721-731` manually resolve dependencies from `RequestServices`.
- `Controllers/HomeController.cs:401-456` exposes a cache-performance test route and reads a session `ContactID`.
- `Extensions/ListManagerCacheExtensions.cs:40-83` caches `ListManager` setup by account and select date.
- `Extensions/ListManagerCacheExtensions.cs:118-139` invalidates cache by account/list prefixes.
- `WebServiceConnector/DownloadIntegrateData.Core.cs:111-124` starts a mixed download flow with account/password, date, list, and weekly report data.
- `WebServiceConnector/UploadIntegrateData.Core.cs:80` uses a static upload lock; `:92-143` runs upload setup and work under mixed state.
- `WebServiceConnector/WeeklyReportManager.cs:328` returns `DownloadWeeklyReport` immediately after upload.

## Dependencies And Consumers

- Upstream consumers: all controllers inheriting `BaseChurchController`, legacy `/Home/*` callers, LINE/LIFF pages still posting to legacy Home endpoints, and ChurchReport views using historical route names.
- Downstream dependencies: ASP.NET Core Session, authentication claims, `IHttpContextAccessor`, `IMemoryCache`, `ICrmConnectionPool`, `IToolUtilityProvider`, `ToolUtilityClass`, CRM organization service, DevExtreme client routes, and per-request `InMemoryContext`.
- Gate status: QUARANTINE. X05Q has no stable contract and must not be optimized as a single module.

## Scope Decision

The diagnosis keeps product files read-only. Findings are framed as boundary defects and extraction candidates, not implementation changes.
