# Phase 3 Non-Prod Tier A Enablement Checklist

Date: 2026-07-25  
Target surface: **Donation fee date-range read only**  
Code path: `DonationFeeQueryService.FillFeeList`  
Switch: `DynamicsAccess:Package01FeeReadsEnabled`

Related docs:

- `phase3-enablement-rollback.md`
- `phase3-package1-consumer-matrix.md`
- `phase3-live-smoke-attempt.md`

## 0. Absolute rules for this tier

1. Do this on **non-production** first (local VS, jesusback, or a staging host). Do **not** open production until Tier A parity is recorded.
2. Keep write paths legacy. Tier A does not enable CreateFee / payment updates.
3. Never copy `CrmConnection:Password` into `DynamicsAccess`.
4. Rollback is always: set `Package01FeeReadsEnabled=false` and restart the product process.
5. Compare **legacy vs Package 1** on the same contact + date window before calling Tier A green.

## 1. Pre-flight (do all boxes)

### 1.1 Config facts

- [ ] `CrmConnection:Organization` matches the org you intend to test (example active cloud: `jesus`)
- [ ] Web API root is **not** Organization.svc  
  Expected shape: `https://<org-host>/api/data/v9.1/` or `/api/data/v8.2/`
- [ ] `DynamicsAccess:ProfileAlias` set (example: `jesus-prod`)
- [ ] `DynamicsAccess:CeVersion` matches Web API version (`9.1` or `8.2`)
- [ ] `DynamicsAccess:Package01FeeReadsEnabled` currently `false` (baseline legacy capture first)
- [ ] Secrets are reference names only (`SecretReference` / env secret **names**)

### 1.2 Mode choice

Pick **one** mode for the non-prod test:

| Mode | When to use | Extra pre-flight |
| --- | --- | --- |
| **Embedded** (recommended for first Tier A in VS) | Single product local debug; no separate Gateway process | Embedded Web API root + credential source valid on this machine |
| **Gateway** | Validate shared platform boundary | Gateway process running; product `Gateway:Endpoint` reachable; Gateway appsettings Web API root matches org |

- [ ] Chosen mode written down: `Embedded` / `Gateway`
- [ ] If Gateway: health URL / process is up before product start
- [ ] If Embedded: host identity or secret env names exist on this machine

### 1.3 Build / unit gate (already green on isolation worktree)

```powershell
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --nologo --no-restore
dotnet test SpeechMessage.Dynamics.SmokeTests\SpeechMessage.Dynamics.SmokeTests.csproj --nologo --no-restore
dotnet build SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj --nologo --no-restore
```

- [ ] Unit tests pass
- [ ] Default smoke pass (live disabled)
- [ ] ChurchReport build pass

### 1.4 Host network / auth gate (must pass before enable)

- [ ] DNS resolves org host
- [ ] TCP 443 to org host succeeds
- [ ] TLS handshake succeeds from **this same host** that will run the product/smoke
- [ ] Auth mode available:
  - Windows HostIdentity: process identity can access CE Web API
  - or SecretReference: username/password/domain secret env vars present (names only in config)

If TLS fails with `SEC_E_NO_CREDENTIALS` / schannel errors, **stop**. Do not enable the flag on that host. See `phase3-live-smoke-attempt.md`.


### 1.4.1 Important host-identity note (this worktree)

- VS2026 ChurchReport login to jesus works under the **operator interactive account**.
- Confirmed visually: VS2026 + IIS Express serving ChurchReport at `http://localhost:43371` login page.
- Codex agent shell may run as `codexsandboxoffline` and fail HTTPS with `SEC_E_NO_CREDENTIALS` even when TCP 443 succeeds.
- Always run live smoke / Tier A CRM checks from **VS Developer PowerShell** (or the same account as ChurchReport), not from the agent sandbox identity.
- Helper: `docs/scripts/Invoke-DynamicsLiveSmoke.ps1 -EnableLive`

## 2. Capture legacy baseline (flag still false)

1. Keep:

```json
"DynamicsAccess": {
  "Package01FeeReadsEnabled": false
}
```

2. Restart ChurchReport.
3. Pick **one known contact** that has dedication fees in a known date window.
4. Open the donation fee query UI / form that uses `DonationFeeQueryService`.
5. Record:

| Field | Value |
| --- | --- |
| Contact Id | |
| Full name | |
| QueryStartDate | |
| QueryEndDate | |
| Returned row count | |
| TotalAmount | |
| First 3 fee Ids / amounts | |
| Approx latency | |
| Trace marker | look for `[DEDQUERY-LEGACY]` |

- [ ] Legacy baseline captured and saved (screenshot or notes)
- [ ] Logs do **not** print CRM password

## 3. Enable Tier A only

### 3.1 Embedded example (local VS first)

```json
"DynamicsAccess": {
  "Package01FeeReadsEnabled": true,
  "ExecutionMode": "Embedded",
  "EnvironmentSuffix": "prod",
  "ProfileAlias": "jesus-prod",
  "CeVersion": "9.1",
  "Embedded": {
    "OrganizationWebApiBaseUri": "https://jesus.speechmessage.com.tw/api/data/v9.1/",
    "CeVersion": "9.1",
    "SecretReference": "dynamics-jesus-prod-credential",
    "ManifestOrRegistrySource": "local-dev-manifest"
  }
}
```

### 3.2 Gateway example (shared pool)

```json
"DynamicsAccess": {
  "Package01FeeReadsEnabled": true,
  "ExecutionMode": "Gateway",
  "ProfileAlias": "jesus-prod",
  "CeVersion": "9.1",
  "Gateway": {
    "Endpoint": "https://localhost:5101/",
    "ApiPrefix": "/v1"
  }
}
```

Then:

- [ ] Start Gateway with matching Web API root / auth
- [ ] Restart ChurchReport
- [ ] Confirm process started without bootstrap exception  
  (missing ProfileAlias / Gateway endpoint should fail fast when flag is true)

## 4. Tier A functional checks

Repeat the **same contact + date window** as section 2.

| Check | Pass criteria |
| --- | --- |
| Trace marker | `[DEDQUERY-P01]` appears (not only LEGACY) |
| Row count | Equal to legacy baseline (or documented acceptable empty if CRM truly has no rows) |
| TotalAmount | Equal to legacy |
| Fee identity | Same fee records (id / amount / date fields that UI shows) |
| Wrong-org leakage | No rows from another organization/profile |
| Error style | Failures are clear errors, not silent empty success when legacy had data |
| Latency | Acceptable vs legacy for operator (record both) |
| Secrets | No password/token in app logs or exception messages |
| Memory / sockets | Repeat the query 20 times; process does not keep growing unbounded |

- [ ] All rows in table marked pass
- [ ] Operator initials / timestamp recorded

## 5. Optional live smoke on a credentialed host

Only after section 1.4 TLS/auth gate is green on that host:

```powershell
$env:DYNAMICS_SMOKE_ENABLED = "1"
$env:DYNAMICS_SMOKE_WEBAPI_ROOT = "https://jesus.speechmessage.com.tw/api/data/v9.1/"
$env:DYNAMICS_SMOKE_CE_VERSION = "9.1"
# HostIdentity default; or:
# $env:DYNAMICS_SMOKE_CREDENTIAL_SOURCE = "SecretReference"
# $env:DYNAMICS_SMOKE_USERNAME_SECRET = "DYNAMICS_JESUS_PROD_USERNAME"
# $env:DYNAMICS_SMOKE_PASSWORD_SECRET = "DYNAMICS_JESUS_PROD_PASSWORD"
# $env:DYNAMICS_SMOKE_DOMAIN_SECRET = "DYNAMICS_JESUS_PROD_DOMAIN"
# optional fee-range:
# $env:DYNAMICS_SMOKE_CONTACT_ID = "<guid>"
# $env:DYNAMICS_SMOKE_PROFILE_ALIAS = "jesus-prod"

dotnet test SpeechMessage.Dynamics.SmokeTests\SpeechMessage.Dynamics.SmokeTests.csproj --nologo --no-restore --filter "FullyQualifiedName~WhoAmI_live_smoke_when_enabled"
```

Expected:

- WhoAmI `Succeeded=true`
- If contact id set: fee date-range operation `Succeeded=true`

- [ ] WhoAmI live smoke pass
- [ ] Fee date-range live smoke pass (if contact provided)

## 6. Rollback drill (required once per host)

Before leaving the machine:

1. Set `Package01FeeReadsEnabled` back to `false`
2. Restart product
3. Re-run the same query
4. Confirm `[DEDQUERY-LEGACY]` returns and UI works

- [ ] Rollback drill completed under 5 minutes
- [ ] Flag left at the intended final state for this host (usually `false` until broader non-prod sign-off)

## 7. Decision gate

| Outcome | Next action |
| --- | --- |
| Tier A pass on non-prod | Proceed to Tier B stor-by-contact on same non-prod host |
| Parity mismatch | Keep flag false; capture both traces; debug Package 1 mapping before retry |
| TLS/auth host broken | Do not enable; move to a credentialed host (see live-smoke attempt notes) |
| Production request | Reject until Tier A-D non-prod green + operator present |

## 8. Operator log template

```
Date:
Host:
Org / ProfileAlias:
Mode (Embedded/Gateway):
ContactId:
Date window:
Legacy count / total:
P01 count / total:
Latency legacy / p01:
WhoAmI smoke:
Rollback drill:
Result: PASS / FAIL
Notes:
```