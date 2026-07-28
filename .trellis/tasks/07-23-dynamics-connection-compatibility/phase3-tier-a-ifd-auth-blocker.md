# Phase 3 Tier A IFD Auth Blocker

Date: 2026-07-25
Updated: 2026-07-25 (authorization_code attempt)

## Confirmed facts from operator environment

### 1) Password grant is disabled
Token endpoint returned:

```text
HTTP 400 unsupported_grant_type
MSIS9611: only supports authorization_code or refresh_token
```

### 2) authorization_code with provisional ClientId fails on ADFS
Operator opened `/diagnostics/adfs-authorize` and ADFS showed error page:

```text
Relying party: Dynamics 365 對外連線 IFD
Activity ID: 00000000-0000-0000-67c3-078004000025
```

Trace confirms ChurchReport did redirect with:

```text
redirectUri=http://localhost:43371/diagnostics/adfs-callback
clientId=2ad88395-b77d-4561-9441-d0e40824f9bc
```

Interpretation:

- Relying party name is the **CRM IFD trust**, not a registered OAuth application name.
- Provisional Dynamics Online sample ClientId is almost certainly **not registered** on this on-prem ADFS.
- Therefore authorization_code cannot complete until ADFS admin registers a client + redirect URI and grants CRM resource permission.

### 3) Legacy SOAP still works
`[DEDQUERY-LEGACY] FullName=胡夢嵩 Start=2026-01-01 End=2026-07-25 Returned=56`

Package01 remains **false**.

## Why Web API is blocked

| Path | Status |
| --- | --- |
| SOAP / WS-Trust (legacy) | Works |
| Web API + Windows NTLM | HTTP 302 IFD |
| Web API + password grant | ADFS rejects grant_type |
| Web API + auth code (sample client id) | ADFS RP error on CRM IFD trust |
| Web API + refresh token | No refresh token yet (needs successful auth code once) |

## Required ADFS admin action

On ADFS server (example for classic AdfsClient):

```powershell
$clientId = [guid]::NewGuid().Guid
Add-AdfsClient `
  -Name "SpeechMessage-ChurchReport-LocalDev" `
  -ClientId $clientId `
  -RedirectUri "http://localhost:43371/diagnostics/adfs-callback"

# Put $clientId into ChurchReport appsettings:
# DynamicsAccess:Embedded:ClientId
```

If the farm uses Application Groups, create:

1. Native/public client with redirect `http://localhost:43371/diagnostics/adfs-callback`
2. Permission to the CRM/IFD Web API resource / relying party identifier for jesus

Then:

1. Update `DynamicsAccess:Embedded:ClientId`
2. Open `/diagnostics/adfs-authorize?go=1`
3. Complete login
4. `/diagnostics/adfs-callback` should save refresh token
5. `/diagnostics/adfs-token-probe` should WhoAmI ok=true
6. Only then enable Package01

## Engineering status

- No-SDK Web API stack is ready
- Package1 fee-read path is ready
- Auth is the remaining Tier A gate
- Cannot invent a working ClientId from app code alone