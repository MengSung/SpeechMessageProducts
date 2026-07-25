# Phase 3 Enablement / Rollback Runbook

Date: 2026-07-25  
Scope: ChurchReport Package 1 **read** paths only  
Default posture: **OFF** (`DynamicsAccess:Package01FeeReadsEnabled = false`)

## 1. What this flag controls

When `Package01FeeReadsEnabled` is `false` (default):

- All Package 1-capable read consumers keep **legacy ToolUtility / SOAP** behavior.
- Gateway / Embedded wiring may still bootstrap, but fee/stor **read switchboard** stays on legacy.

When set to `true`:

- Only the **migrated Package 1 read paths** switch to no-SDK ProductClient (Gateway or Embedded).
- **Write paths stay on legacy** (CreateFee, CreateNewStorLesson, payment-side stor updates, etc.).
- Option-set metadata / Package 0 product wiring is **not** covered by this flag yet.

## 2. Safe enable tiers (non-production first)

Never enable production until Tier A + Tier B pass on a non-prod / jesusback-style host with known credentials and network path to Web API.

| Tier | Surfaces | Why this order | Required checks before next tier |
| --- | --- | --- | --- |
| **A (first)** | Donation fee date-range read (`DonationFeeQueryService`) | Single, high-value read; easy to compare against legacy result | Same contact/date window returns equivalent fee rows; no password leakage in logs; latency acceptable |
| **B** | Equipment / MemberInfo / DownloadEquipment / EquipmentStatusCalculator stor-by-contact | Same Package 1 template, broader UI | Stor list parity for 2–3 known contacts; no session growth on repeated page loads |
| **C** | FeeDownUpLoader present / enroll / process by discipleLesson | Still read-only, but more processor-coupled | Processor counts and present lists match legacy for one known disciple lesson batch |
| **D (last among Package 1 reads)** | PollManager / QrCodeUtility find contact+lesson | User-facing sign-in / poll paths; higher blast radius | Sign-in lookup returns the expected stor lesson only; fail closed to clear error (not empty success) |

**Production rule:** only after Tier A–D green on non-prod, and only with an operator present who can flip the flag back within minutes.

## 3. Pre-flight checklist (any environment)

1. Confirm org facts from product config (do **not** copy passwords into `DynamicsAccess`):
   - Organization: `jesus` (active cloud in ChurchReport)
   - Legacy SOAP (legacy only): `https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc`
   - Web API root: `https://jesus.speechmessage.com.tw/api/data/v9.1/`
2. Confirm `DynamicsAccess` profile:
   - `ProfileAlias`: `jesus-prod`
   - `CeVersion`: `9.1`
   - `ExecutionMode`: `Gateway` (default shared) or `Embedded` (local VS debug exception)
3. Confirm secrets are **references only** (`SecretReference` / env secret **names**). Never put `CrmConnection:Password` into `DynamicsAccess`.
4. Confirm project boundary:
   - ChurchReport may reference: Abstractions + ProductClient + Embedded
   - ChurchReport must **not** take a direct `ProjectReference` to `SpeechMessage.Dynamics.WebApi`
5. Confirm Gateway is reachable if mode is Gateway (`Endpoint`, e.g. `https://localhost:5101/`, `ApiPrefix` `/v1`).
6. Run local verification (this host used `--no-restore` because NuGet.Config was sandbox-blocked):

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --nologo --no-restore
dotnet test SpeechMessage.Dynamics.SmokeTests\SpeechMessage.Dynamics.SmokeTests.csproj --nologo --no-restore
dotnet build SpeechMessage.Dynamics.Gateway\SpeechMessage.Dynamics.Gateway.csproj --nologo --no-restore
dotnet build SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj --nologo --no-restore
```

## 4. Enable steps (non-prod)

### 4.1 Shared Gateway mode (recommended platform default)

Edit product `appsettings` / environment-specific override (ChurchReport example shape):

```json
"DynamicsAccess": {
  "Package01FeeReadsEnabled": true,
  "ExecutionMode": "Gateway",
  "EnvironmentSuffix": "prod",
  "ProfileAlias": "jesus-prod",
  "CeVersion": "9.1",
  "Gateway": {
    "Endpoint": "https://localhost:5101/",
    "ApiPrefix": "/v1"
  },
  "Embedded": {
    "OrganizationWebApiBaseUri": "https://jesus.speechmessage.com.tw/api/data/v9.1/",
    "CeVersion": "9.1",
    "SecretReference": "dynamics-jesus-prod-credential",
    "ManifestOrRegistrySource": "local-dev-manifest"
  }
}
```

Then:

1. Start Gateway with matching `DynamicsWebApi` root / auth secret **names**.
2. Restart ChurchReport so DI bootstrap reloads `DynamicsAccess`.
3. Exercise **Tier A only** first.
4. Compare results with a second process that still has the flag `false` if possible.

### 4.2 Embedded mode (product-local exception)

Use when you want VS 2026 local debug without a separate Gateway process:

```json
"DynamicsAccess": {
  "Package01FeeReadsEnabled": true,
  "ExecutionMode": "Embedded",
  "ProfileAlias": "jesus-prod",
  "Embedded": {
    "OrganizationWebApiBaseUri": "https://jesus.speechmessage.com.tw/api/data/v9.1/",
    "CeVersion": "9.1",
    "SecretReference": "dynamics-jesus-prod-credential",
    "ManifestOrRegistrySource": "local-dev-manifest"
  }
}
```

Embedded still must not put plaintext CRM passwords into JSON if the secret store path is available. Prefer host identity / secret names.

### 4.3 Live smoke (only on a host that can reach CRM)

```powershell
$env:DYNAMICS_SMOKE_ENABLED = "1"
$env:DYNAMICS_SMOKE_WEBAPI_ROOT = "https://jesus.speechmessage.com.tw/api/data/v9.1/"
$env:DYNAMICS_SMOKE_CE_VERSION = "9.1"
# optional: $env:DYNAMICS_SMOKE_CONTACT_ID = "<guid>"
dotnet test SpeechMessage.Dynamics.SmokeTests\SpeechMessage.Dynamics.SmokeTests.csproj --nologo --no-restore
```

If the host cannot resolve/authenticate to `jesus`, leave live smoke disabled. Default smoke suite already passes without live CRM.

## 5. Rollback (immediate)

Rollback is intentionally one switch:

```json
"DynamicsAccess": {
  "Package01FeeReadsEnabled": false
}
```

Then restart the product process.

Effects:

- All Package 1 read consumers return to legacy ToolUtility paths.
- No schema migration, no data rewrite, no Gateway uninstall required.
- Write paths were never switched by this flag, so they remain unchanged.

If Gateway is unhealthy but Embedded is configured, you may temporarily set `ExecutionMode` to `Embedded` **only in non-prod** for diagnosis; production preferred rollback is still **flag off**.

## 6. What is / is not covered when enabled

### Covered when `true` (Package 1 reads wired)

- Donation fee retrieve by contact + date range
- Stor lessons by contact (Equipment / MemberInfo / DownloadEquipment / EquipmentStatusCalculator)
- Stor lessons by discipleLesson (FeeDownUpLoader present/enroll/process reads)
- Poll / QR contact+lesson find (list + filter)

### Not covered (still legacy even when flag is true)

- Fee create / update payment fields
- CreateNewStorLesson
- Donation payment processor stor updates
- Option-set metadata product wiring
- Arbitrary disciple-lesson entity date lists outside Package 1 templates
- Removal of `PowerPlatform.Dataverse.Client` / CRM SDK references (Phase 6 only)

## 7. Failure signals → rollback now

Flip `Package01FeeReadsEnabled` to `false` immediately if any of these appear:

- Empty success where legacy would return rows (false negative)
- Wrong contact/org data mixed across profiles (session leakage class)
- Unbounded memory / connection growth under repeated page refresh
- Gateway 5xx storm or admission rejections that break user flows
- Any attempt by product code to call WebApi types directly (architecture breach)

## 8. Operator decision matrix

| Goal | Setting |
| --- | --- |
| Safest production default | `Package01FeeReadsEnabled=false` |
| Shared platform pool for many products | `ExecutionMode=Gateway` |
| Single product local debug in VS | `ExecutionMode=Embedded` |
| Instant undo | flag `false` + restart product |
| Delete SDK / GitHub borrowed client | **Not this runbook** — wait for Phase 6 after all consumers migrate |

## 9. Stabilization gate status

This runbook is the Phase 3 **stabilization** artifact. Do not expand write paths or delete SDK while this gate is the active checkpoint.

## 10. Operator deep-dive for Tier A

- Detailed non-prod checklist: `phase3-tier-a-enablement-checklist.md`
- Live smoke host probe log: `phase3-live-smoke-attempt.md`
