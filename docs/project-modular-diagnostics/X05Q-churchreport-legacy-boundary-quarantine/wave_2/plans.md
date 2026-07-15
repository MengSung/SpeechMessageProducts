# Wave 2 Plan: X05Q Session Identity Boundary Quarantine

CONTRACT_STATUS: WAVE_PLAN_APPROVED
WAVE_ID: Wave 2
WORKSPACE: X05Q-churchreport-legacy-boundary-quarantine
SELECTED_ISSUES: X05Q-SEC-001

## Approval Record

- Claude-only self-healing review result: `CLAUDE_UNAVAILABLE`. Both Claude attempts returned `no-usable-output`; therefore there were no usable Claude findings. Artifact: `.ccg/dual-model-runs/20260715-115151-wave2-x05q-contract-reviewer/summary.json`.
- Exactly one permitted controller-dispatched read-only Codex fallback re-review approved this X05Q Wave 2 contract for exactly `X05Q-SEC-001`, with no unresolved Critical or Warning findings.
- This is document-contract approval only. It does not satisfy any local, staging, runtime, rollout, rollback, or deployment proof gate; all proof and `BLOCKED` conditions in this contract remain in force until separately evidenced.

## Scope

This wave is an immutable repair contract for exactly `X05Q-SEC-001`: session identity fallback remains inside the quarantine boundary. It may define the future repair scope, validation matrix, counters, and rollback boundary. It does not authorize product code edits during planning.

Explicitly excluded:

- `X05Q-SEC-002` and any `/Home/*` compatibility facade work.
- `X05Q-PERF-001`, `X05Q-PERF-002`, `X05Q-PERF-003`, and any performance optimization.
- Any B01/B02/B03/B05/B07/X01/X04A module repair except consuming the final identity contract in a later approved wave.
- Any route rewrite, broad `BaseChurchController` rewrite, CRM schema change, auth ticket schema change, session key rename, cache key migration, or secret/configuration change.

## Future Repair Allowlist

The repair owner may only touch the paths and symbols below unless a later owner approval amends this contract.

### Product Paths

- `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs`
  - `EnsureCorrectUserData()`
  - `ValidateSession()`
  - `RegenerateSessionId()`
  - `IssueAuthTicketAsync(string contactId, string account, string passwordKey, string loginType)`
  - existing reads/writes of `_SessionUserId`, `_SessionCreatedAt`, `_LoginAccount`, `_LoginPassword`, `_MemberInfoAccess`
  - existing `InMemoryContext.ListManager` identity and hydration calls only as adapter wiring points
- `SpeechMessageProducts.ChurchReport/Security/LoginClaimsFactory.cs`
  - `ContactIdClaim`
  - `AccountClaim`
  - `PasswordKeyClaim`
  - `LoginTypeClaim`
  - `Build(string contactId, string account, string passwordKey, string loginType)`
- `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs`
  - `SetupListManagerWithCache(...)`
  - `RestoreFromCache(...)`
  - `CachedListManagerData.CachedAt`
  - account/date-scoped cache metadata needed to reject mismatched, stale, or expired cached ListManager state
- `SpeechMessageProducts.ChurchReport/Models/ListManager.cs`
  - `SetupListManager(string Account, string Password, DateTime aSelectDate, IOrganizationService organizationService = null)`
  - `m_Account`
  - `m_Password`
  - `m_SelectDate`
  - `LoginType`
  - `LoginFullName`
  - `ActiveListId`

### Test Paths

- `ChurchReport.MemberInfo.Tests/Security/LoginClaimsFactoryTests.cs`
- New or existing tests in `ChurchReport.MemberInfo.Tests/**` whose names and fixtures are limited to the `LegacySessionIdentityAdapter` decision contract, fake session/cache/ListManager/CRM counters, and the scenario matrix in `measurements.md`.

### Configuration and Consumer Paths

No configuration path is authorized for this wave.

No consumer module path is authorized for this wave. Existing ChurchReport routes may only be exercised by tests or staging proof; their source files are not in the repair write allowlist.

## Current Source Evidence

The selected issue is source-confirmed in the current legacy boundary:

- `BaseChurchController.EnsureCorrectUserData()` reads `_LoginPassword` and `_LoginAccount`, compares them with `InMemoryContext.ListManager.m_Password`, may call `InMemoryContext.ListManager.SetupListManager(...)`, and may write `_LoginAccount`/`_LoginPassword` for LINE fallback.
- `BaseChurchController.ValidateSession()` reads `_SessionUserId` and `_SessionCreatedAt`, applies an eight-hour age check, and requires `InMemoryContext.ListManager.m_Account` to be present.
- `BaseChurchController.RegenerateSessionId()` clears session, commits, then restores `_SessionUserId`, `_SessionIdentifier`, `_SessionCreatedAt`, `_SessionUserAgent`, and `_SessionRealIp`; its diagnostic text states ASP.NET Core does not rotate the Session ID there and identity is bound to the auth ticket.
- `BaseChurchController.IssueAuthTicketAsync(...)` issues a cookie principal built by `LoginClaimsFactory`.
- `LoginClaimsFactory` emits `church:contactId`, `church:account`, `church:pwdkey`, and `church:loginType`.
- `ListManagerCacheExtensions.SetupListManagerWithCache(...)` restores cached ListManager state by account/date key or calls `SetupListManager(...)` and writes cached data; cached data currently carries `CachedAt` but no explicit authenticated-principal owner fingerprint.
- `ListManager.SetupListManager(...)` assigns `m_Account`, `m_Password`, `m_SelectDate`, then hydrates CRM-backed list state through `DownloadListManager.GetListManager(...)`.

## Required Boundary Contract

Future repair must introduce or emulate a typed `LegacySessionIdentityAdapter` decision boundary behind existing legacy controller entrypoints. The adapter must not create an independent authorization source; it only translates authenticated principal plus compatibility inputs into a single immutable per-request decision.

Authority order:

1. Authenticated server principal is the only authorization authority.
2. Session identity, account, password key, login mode, and `_MemberInfoAccess` are compatibility inputs that must match the authenticated principal before use.
3. LINE password key and `LineIdLogin` account mode are compatibility inputs only when the authenticated principal has `LoginTypeClaim == "LINE"` and matching owner metadata.
4. Cached ListManager state is a bounded candidate only when owner fingerprint, account mode, selected date, and expiry metadata match the authenticated principal and live compatibility inputs.
5. CRM/ListManager rehydration is permitted only after the adapter returns a bounded `Rehydrate` decision for an authenticated principal with matching compatibility inputs.

Required typed inputs:

- server principal snapshot: authenticated flag, contact id, account, password key, login type;
- session snapshot: `_SessionUserId`, `_SessionCreatedAt`, `_LoginAccount`, `_LoginPassword`, `_MemberInfoAccess`;
- current ListManager snapshot: `m_Account`, `m_Password`, `m_SelectDate`, `LoginType`, `ActiveListId`;
- cache snapshot: account/date key, owner fingerprint, account mode, selected date, created-at/expiry, payload presence;
- bounded CRM/ListManager rehydration request: account, password key, login mode, selected date, owner fingerprint, maximum attempt count.

Required immutable outputs:

- `Allow`: principal and compatibility state match; no rehydrate is needed.
- `Reject`: unauthenticated, missing required identity, session expired, owner mismatch, account-mode mismatch, LINE mismatch, cache owner mismatch, stale/expired invalid state, CRM failure, or partial rehydrate failure.
- `Rehydrate`: principal is authenticated, compatibility inputs match, cache cannot be used, and exactly one bounded ListManager/CRM setup attempt is permitted.
- `CacheHit`: cache metadata fully matches principal, account mode, selected date, and expiry; no CRM setup is permitted.

Reject rules:

- A reject must not call `SetupListManager(...)`.
- A reject must not contact CRM for identity rehydration.
- A reject must not write, clear, or remove session values.
- A reject must not write, clear, restore, or invalidate shared cache.
- A reject must not mutate `InMemoryContext.ListManager`.
- A reject must not update `_MemberInfoAccess`.

Rehydrate rules:

- Rehydrate must be bounded to at most one ListManager setup and at most one CRM-backed setup per request.
- Session/cache/ListManager commit must be atomic from the adapter caller's perspective: commit only after the full hydration succeeds and owner/mode/expiry metadata is complete.
- CRM failure, timeout, missing record, or partial payload must produce `Reject` with zero partial session/cache/ListManager commit.

## Repair Sequence

1. Add focused decision-contract tests first, using synthetic principals, fake session/cache/ListManager/CRM counters, and no live CRM.
2. Add the typed adapter or equivalent internal boundary with immutable input/output models.
3. Wire `BaseChurchController.EnsureCorrectUserData()` and related validation points through the adapter without changing legacy route templates or controller inheritance.
4. Preserve legacy routes through the adapter: compatible authenticated account and LINE requests keep observable status/redirect behavior.
5. Add redacted counters for decision, reject reason, rehydrate attempt, cache read/hit/write/mutation, session read/write/clear, ListManager setup, and CRM setup.
6. Run the local proof in `measurements.md`; staging/runtime proof remains blocked until the required environment exists.

## Local Validation Commands

Run from the repository root:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LoginClaimsFactory|FullyQualifiedName~LegacySessionIdentity|FullyQualifiedName~SessionIdentity"
dotnet build .\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj --no-restore
rg -n "EnsureCorrectUserData|ValidateSession|RegenerateSessionId|IssueAuthTicketAsync|SetupListManager|_LoginAccount|_LoginPassword|_SessionUserId|_MemberInfoAccess" .\SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs .\SpeechMessageProducts.ChurchReport\Security\LoginClaimsFactory.cs .\SpeechMessageProducts.ChurchReport\Extensions\ListManagerCacheExtensions.cs .\SpeechMessageProducts.ChurchReport\Models\ListManager.cs
```

Expected evidence:

- all scenario tests in `measurements.md` pass with exact decision, status/redirect, reason, and counter values;
- build passes;
- grep output remains within the future repair allowlist or is explicitly justified in review;
- logs and test output contain only synthetic labels or redacted hashes, never raw account, password key, LINE id, contact id, cookie, token, or CRM payload.

## Rollback Boundary

Rollback scope is limited to the adapter, adapter wiring in `BaseChurchController`, associated decision tests, and any allowed cache metadata added for this issue. Rollback must preserve existing route templates, controller inheritance, auth claim names, session key names, CRM schema, cache key compatibility, and current legacy entrypoints. If rollback requires touching excluded issues or modules, the wave is unsuccessful and must be marked blocked instead of expanded.
