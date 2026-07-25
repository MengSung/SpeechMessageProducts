# Phase 3 Tier A IFD Auth Blocker

Date: 2026-07-25  
Updated: 2026-07-25 (ADFS wiring in progress)

## Symptom

With Package01 enabled (Embedded + SecretReference Windows credentials + Web API v8.2):

```text
Package 1 read failed: dynamics.upstream.failure
Operation 'fee.dedication.retrieve.by.contact.date.range' failed with HTTP 302.
```

## Root cause

`jesus` is **Dynamics 365 CE 8.2 IFD / claims-based**.

| Path | Auth | Result |
| --- | --- | --- |
| Legacy ToolUtility / OnPremiseClient SOAP `Organization.svc` | WS-Trust username/password (claims) | Works (56 fee rows baseline) |
| Package 1 Web API `.../api/data/v8.2/` | Windows NTLM NetworkCredential | **HTTP 302** redirect (login/ADFS challenge) |

## Operator-confirmed ADFS

- Authority: `https://speechmessagests.speechmessage.com.tw/adfs`
- Token endpoint: `https://speechmessagests.speechmessage.com.tw/adfs/oauth2/token`
- Resource (CRM): `https://jesus.speechmessage.com.tw/`
- Web API root: `https://jesus.speechmessage.com.tw/api/data/v8.2/`

## Engineering progress (this checkpoint)

Implemented end-to-end AdfsOAuth wiring:

1. `AdfsOAuthTokenProvider` (password grant + bearer secret + cache; no factory client dispose leak)
2. `DynamicsWebApiClient` Authorization: Bearer path
3. Embedded DI maps `AuthMode/AuthorityUri/ResourceUri/ClientId/AllowLocalDevPasswordGrant`
4. ChurchReport bootstrap binds ADFS fields; local-dev secret bridge still uses CrmConnection names only
5. appsettings prepared with `AuthMode=AdfsOAuth` and provisional ClientId
6. Unit tests for token provider + client constructor fixes
7. Operator probe script: `docs/scripts/Invoke-AdfsTokenProbe.ps1`

## Current config posture

`Package01FeeReadsEnabled` remains **`false`** until token+WhoAmI is proven under the VS operator identity.

Provisional ClientId currently set to Dynamics CRM well-known id:

```text
2ad88395-b77d-4561-9441-d0e40824f9bc
```

This may be rejected by ADFS if that client is not registered. If probe fails on invalid_client, replace with the real ADFS application/client id.

## Immediate operator next step

In the same Windows account that can F5 ChurchReport / login jesus:

```powershell
cd "D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree"
powershell -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Invoke-AdfsTokenProbe.ps1
```

Pass criteria:

1. TOKEN OK
2. WhoAmI OK

Then enable Package01 and compare fee rows to legacy baseline `Returned=56`.

## Tier A status

| Item | Status |
| --- | --- |
| Legacy baseline 56 rows | PASS |
| ADFS authority known | PASS |
| AdfsOAuth code wiring | PASS (this checkpoint) |
| Live ADFS token + WhoAmI | PENDING operator probe (agent TLS blocked) |
| Package01 fee parity 56 | PENDING after token proof |
| Tier A full pass | NOT YET |