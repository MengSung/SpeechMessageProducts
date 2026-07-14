# X05Q Extraction Analysis

Module: X05Q
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Extraction Candidate 1: Legacy Session Identity Adapter

Owning files:

- `Controllers/BaseChurchController.cs`
- `Extensions/ListManagerCacheExtensions.cs`
- Consumers in all `BaseChurchController` subclasses.

Contract:

- Input: auth principal, ASP.NET Session, request metadata, current `InMemoryContext`, optional LINE identity.
- Output: typed `LegacyUserContext` with account mode, user id, CRM contact id, access level, selected date, cache key, and validation result.
- Dependencies: `IHttpContextAccessor`, session, claims, `ICacheService` or `IMemoryCache`, `ICrmConnectionPool`, `IToolUtilityProvider`.
- Test seam: pure validation rules for session keys, claim/session mismatch, expired session, cache hit/miss, and LineIdLogin fallback.
- Rollback boundary: keep existing base controller calls and route them through the adapter without changing controller routes first.

Why it is valuable:

This directly reduces the highest security risk and removes duplicated validation/rehydration logic. It is the most valuable extraction because many business modules inherit the current base controller.

## Extraction Candidate 2: Legacy Home Compatibility Facade

Owning files:

- `Controllers/HomeController.cs`
- Related legacy `Views/Home/**` callers only after owner proof.

Contract:

- Input: legacy route name, route values, HTTP method, auth/session state.
- Output: redirect or delegated action target with explicit owner module and validation preconditions.
- Dependencies: ASP.NET routing, anti-forgery policy, auth/session adapter, downstream controller/action manifest.
- Test seam: route manifest tests that prove every `/Home/*` route maps to one owner or stays X05Q.
- Rollback boundary: preserve existing route templates while replacing method bodies with manifest-driven redirects.

Why it is valuable:

It reduces dangerous compatibility surface and creates a repeatable way to move proven routes to B modules without breaking old URLs.

## Extraction Candidate 3: CRM Boundary Batch Query And Converter Facade

Owning files:

- `WebServiceConnector/DownloadIntegrateData.*`
- `WebServiceConnector/UploadIntegrateData.*`
- `WebServiceConnector/ChurchListDataProcessor.cs`
- `WebServiceConnector/WeeklyReportManager.cs`
- Related converter classes.

Contract:

- Input: typed query request with account context, list id, date/week, selected columns, and pagination/cancellation policy.
- Output: typed DTO collections for list hierarchy, members, present records, weekly reports, and upload results.
- Dependencies: CRM organization service, option-set metadata service, converter functions.
- Test seam: pure converter tests plus fake CRM query service call-count tests.
- Rollback boundary: introduce facade methods alongside existing connector methods, then migrate one consumer at a time.

Why it is valuable:

This is the clearest performance acceleration path. It also makes ownership proofs easier because each query/converter can be assigned to a business module after dependencies are explicit.

## Extraction Candidate 4: Automated Boundary Audit

Inputs:

- Module map explicit owners.
- `rg` caller/callee scans for `BaseChurchController`, `/Home/`, `RequestServices.GetService`, session key names, and `Account` / `Password` parameters.
- Route templates and view links.

Outputs:

- List of files remaining in X05Q with reason.
- Candidate owner handoff.
- Missing guard/test evidence.
- Secret/session/config usage matrix.

Why it is valuable:

X05Q is a quarantine bucket; its success metric is shrinking it with proof. An automated audit prevents regressions where new unowned ChurchReport files silently enter the catch-all.

## Not Recommended

Do not create a single `X05QLegacyService` or optimize the whole X05Q surface. The map explicitly forbids whole-quarantine optimization, and doing so would preserve the mixed boundary under a new name.
