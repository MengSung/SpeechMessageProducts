# X05Q Security Analysis

Module: X05Q
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## SEC-001 Session Identity Fallback Boundary

Evidence:

- `Controllers/BaseChurchController.cs:558-592` stores `_MemberInfoAccess` in Session and derives it from `InMemoryContext.PersonalInfomationModel` or `ListManager.LoginType`.
- `Controllers/BaseChurchController.cs:641-764` reconciles Session password/account with `InMemoryContext.ListManager` and can rebuild the ListManager from session values.
- `Controllers/BaseChurchController.cs:743-764` accepts authenticated LINE claim data to set `ListManager` and writes `_LoginAccount` / `_LoginPassword` back to Session.
- `Controllers/BaseChurchController.cs:978-998` clears and restores session fields, but explicitly logs that ASP.NET Core does not rotate the Session ID.
- `Controllers/BaseChurchController.cs:1012` signs in with claims after the same base controller has manipulated session fields.

Security impact:

The quarantine boundary still couples authentication identity, ASP.NET Session, cached ListManager state, and legacy account/password semantics. A caller that enters through a legacy or LINE-compatible route can rely on fallback reconstruction instead of a single explicit authorization contract. This is a Critical security issue because the boundary owns the shared state mutation path used by multiple business modules and because session rotation is not actually achieved where the method name suggests it.

Required guard:

Extract a single `LegacySessionIdentityAdapter` contract that validates the auth ticket, LINE claim, session keys, account/password mode, and ListManager owner in one place. Until that exists, no X05Q file should be reclassified or optimized based only on filename.

## SEC-002 Dangerous Compatibility Route Surface

Evidence:

- `Controllers/HomeController.cs:65-390` preserves many `/Home/*` compatibility routes, including login, LINE binding, payment, fee, dedication, and scheduler redirects.
- `Controllers/HomeController.cs:80-88`, `:197-202`, `:218-222`, `:237-245`, `:659-666`, and `:721-731` manually resolve dependencies from `HttpContext.RequestServices`.
- `Controllers/HomeController.cs:149-153` forwards `LineId` route data to payment/dedication flows.
- `Controllers/HomeController.cs:401-456` contains a test/cache-performance endpoint in the legacy controller and reads session `ContactID`.

Security impact:

The legacy route facade is not only a redirect table. Some routes instantiate or delegate into controller logic through service locator patterns and pass user/session/LINE data into downstream controllers. This keeps a broad compatibility surface alive at a cross-business boundary. The risk is not that every route is exploitable; the issue is that the compatibility entrypoint bypasses a documented boundary contract for auth, anti-forgery, route ownership, and service lifetime.

Required guard:

Replace ad hoc Home compatibility methods with a route manifest that records owner module, allowed HTTP methods, required auth/session preconditions, accepted parameters, and target action. Anything without an owner should remain X05Q and be blocked from optimization.

## SEC-003 Config/Secret Boundary Visibility

Evidence:

- `appsettings.json:170` and `:174` contain LINE channel access token values.
- `appsettings.json:251` contains a CRM password value.
- X04A owns runtime configuration and secrets, but X05Q legacy code still reads, propagates, or depends on account/password/session flows across controllers and WebServiceConnector classes.

Security impact:

Secret storage is formally X04A, but X05Q is still a consumer of legacy credential-like fields (`Account`, `Password`, LineId-as-password mode, and session login keys). This creates a boundary handoff risk: secret/config hardening cannot be validated unless the legacy boundary declares exactly which credential-like values are acceptable inputs and where they may be logged, cached, or rehydrated.

Required guard:

Add an audit matrix for session keys, config keys, and credential-like method parameters before any extraction. Treat `Password` in `LineIdLogin` flows as identity material, not a harmless string.

## Rejected Security Candidates

- `wwwroot/js/TreeView.js` has no direct token/session evidence in the inspected lines. It remains an ownership/quarantine item, not a confirmed security issue.
- `Services/Navigation/INavigationService.cs` is an interface-only contract in the inspected scope and does not by itself prove a security exposure.
