# Phase 3 Live Smoke Attempt Log

Date: 2026-07-25  
Host: isolation worktree  
Goal: verify jesus CE Web API reachability and run live WhoAmI smoke when possible

## Current conclusion

**Machine can reach jesus for ChurchReport (confirmed by operator via VS2026 login).**  
**Codex agent process on this host cannot complete HTTPS/TLS to jesus.**

So:

- Do **not** treat the agent TLS failure as "jesus is down".
- Do **not** treat it as a Dynamics Package 1 product bug.
- Live smoke / Tier A real CRM verification must run under the **same identity that VS2026 uses** (your interactive developer account), not under the Codex sandbox offline identity.

## Evidence matrix

| Path | Identity / surface | Result |
| --- | --- | --- |
| VS2026 ChurchReport login to jesus organization | Operator interactive session | **Works** (operator confirmed 2026-07-25) |
| DNS `jesus.speechmessage.com.tw` | Agent process | Resolves to `202.153.204.60` |
| TCP 443 | Agent process | **Succeeded** |
| HTTPS via curl/IWR/.NET from agent | `lenovo-legion\codexsandboxoffline` | **Failed** |
| Failure detail | schannel | `SEC_E_NO_CREDENTIALS (0x8009030e)` / "安全性封裝沒有可供使用的認證" |
| WinHTTP proxy | Agent process | Direct access (no proxy) |
| Cert stores visible to agent | CurrentUser Root/My, LocalMachine Root | Readable counts exist; still no usable TLS client credential context for this identity |
| Escalated (unsandboxed) retest | Auto-approval | **Blocked** by account balance / review service; could not re-run outside sandbox identity |

## Why VS works but agent smoke does not

ChurchReport in VS2026 typically runs as your normal Windows user and can:

1. complete schannel TLS with a normal user credential context
2. authenticate to CE with the configured CRM credentials / Windows identity used by the app

The Codex shell here runs as:

```text
lenovo-legion\codexsandboxoffline
```

That identity can open TCP 443, but cannot acquire schannel credentials for HTTPS. This is an **agent runtime isolation** limit, not proof that the org endpoint is unreachable from the PC.

## What to run in VS2026 Developer PowerShell (your account)

Open **the same machine/account that already logs into ChurchReport successfully**, then from the worktree root:

```powershell
# Optional: confirm HTTPS from YOUR identity first
curl.exe -I --max-time 20 https://jesus.speechmessage.com.tw/
# or
Invoke-WebRequest -Uri "https://jesus.speechmessage.com.tw/" -Method Head -UseBasicParsing

# Live WhoAmI smoke (HostIdentity default)
$env:DYNAMICS_SMOKE_ENABLED = "1"
$env:DYNAMICS_SMOKE_WEBAPI_ROOT = "https://jesus.speechmessage.com.tw/api/data/v9.1/"
$env:DYNAMICS_SMOKE_CE_VERSION = "9.1"
# If HostIdentity is not enough and you use secret env vars:
# $env:DYNAMICS_SMOKE_CREDENTIAL_SOURCE = "SecretReference"
# $env:DYNAMICS_SMOKE_USERNAME_SECRET = "DYNAMICS_JESUS_PROD_USERNAME"
# $env:DYNAMICS_SMOKE_PASSWORD_SECRET = "DYNAMICS_JESUS_PROD_PASSWORD"
# $env:DYNAMICS_SMOKE_DOMAIN_SECRET = "DYNAMICS_JESUS_PROD_DOMAIN"
# $env:DYNAMICS_JESUS_PROD_USERNAME = "DOMAIN\user"
# $env:DYNAMICS_JESUS_PROD_PASSWORD = "<do-not-commit>"
# $env:DYNAMICS_JESUS_PROD_DOMAIN = "DOMAIN"

dotnet test "SpeechMessage.Dynamics.SmokeTests\SpeechMessage.Dynamics.SmokeTests.csproj" --nologo --no-restore
```

Optional fee-range smoke after WhoAmI is green:

```powershell
$env:DYNAMICS_SMOKE_CONTACT_ID = "<known-contact-guid>"
$env:DYNAMICS_SMOKE_PROFILE_ALIAS = "jesus-prod"
dotnet test "SpeechMessage.Dynamics.SmokeTests\SpeechMessage.Dynamics.SmokeTests.csproj" --nologo --no-restore
```

Paste the test output back here and this log can be updated from "blocked in agent" to "live green under operator identity".

## Helper script

Also provided:

`docs/scripts/Invoke-DynamicsLiveSmoke.ps1`

It only enables live smoke when you pass `-EnableLive`, defaults to jesus v9.1 root, and refuses to print secret values.

## Relation to Tier A

Use `phase3-tier-a-enablement-checklist.md`.

Because VS2026 already proves product-host connectivity to jesus:

1. Section 1.4 network gate is **pass for your VS identity**
2. Section 1.4 remains **fail for Codex agent identity**
3. Tier A enable/compare should be done in VS2026 / operator terminal, not expected to succeed inside this sandbox shell

## Operator visual confirmation (2026-07-25)

Operator provided a VS 2026 screenshot showing:

- Solution/worktree active: `1.0.0.2.IsolateConnector.Worktree`
- Debug process: `iisexpress.exe`
- Startup project: `SpeechMessageProducts.ChurchReport`
- Browser URL: `http://localhost:43371`
- ChurchReport login page rendered (`會員登入`, account field visible)

Interpretation:

1. **App host path works** under VS/IIS Express on this PC.
2. That path uses the interactive developer identity + ChurchReport configured CRM connection, not the Codex sandbox identity.
3. Therefore jesus connectivity should be validated through:
   - ChurchReport login + Tier A donation fee query in this IIS Express session, and/or
   - `docs/scripts/Invoke-DynamicsLiveSmoke.ps1 -EnableLive` from **VS Developer PowerShell** under the same account
4. Agent-side HTTPS failure remains an isolated runtime limit (`codexsandboxoffline` / schannel credentials), not a contradiction of this screenshot.

## Recommended immediate operator path (no agent TLS needed)

1. Keep IIS Express ChurchReport running as in the screenshot.
2. Log in with a known test account (same one you already use successfully).
3. With `DynamicsAccess:Package01FeeReadsEnabled=false`, open donation fee query and capture legacy baseline (`[DEDQUERY-LEGACY]`).
4. Enable Tier A only (`Package01FeeReadsEnabled=true`, prefer `ExecutionMode=Embedded` first while debugging in VS).
5. Restart/F5, repeat same contact/date window, confirm `[DEDQUERY-P01]` and parity.
6. Optionally run live smoke script from VS terminal in parallel.

## Tier A live judgment (2026-07-25 afternoon)

### What is proven

1. IIS Express app is up on `http://localhost:43371`.
2. Operator login as account `zz` succeeds (Trace.log: user 胡夢嵩, MultiGroupView).
3. Login path uses CRM and returns live org data (small-group dashboard total people = 28 in operator screenshot).
4. `DynamicsAccess:Package01FeeReadsEnabled` is still **false** in appsettings => fee reads remain on **legacy** path only.
5. Agent unauthenticated GET `/Dedication/DedicationFeeView` produced:
   - `[DEDQUERY-LEGACY] FullName= Start=2026-07-25 End=2026-07-25 Returned=0`
   - This is **not** a valid Tier A baseline (no authenticated contact context).

### What is NOT proven yet

1. Authenticated open of **奉獻收費清單** (`/Dedication/DedicationFeeViewWeb`) under the logged-in session.
2. Legacy fee row count / total amount for contact 胡夢嵩 (or zz's contact) over a chosen date range.
3. Package01 path (`[DEDQUERY-P01]`) comparison after enabling the flag.
4. Therefore **Tier A cannot be marked PASS** yet.

### Verdict

- **Connectivity / login readiness: PASS**
- **Tier A Package 1 fee-read gate: NOT PASS (incomplete)**

### Why agent cannot finish alone right now

- Browser session cookies are not shared with the Codex shell.
- `ProcessLogin` takes ~25-40s; short agent command windows timed out before a scripted login completed.
- Password for `zz` is not available to the agent (and should not be committed to docs).

### One-click operator action to finish the missing proof

While still logged in at localhost:43371:

1. Click left menu **奉獻收費清單**
2. Keep current year/date range or set a range that should have fees
3. Tell Codex "已點開" — agent will read Trace.log for `[DEDQUERY-LEGACY] ... Returned=N`
