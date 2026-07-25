# ChurchReport CrmConnection alignment verification

Date: 2026-07-25

## What changed

1. Added `DynamicsProfileAlignment` helper:
   - `jesus` + `prod` => `jesus-prod`
   - Organization.svc URL => organization base URI
   - base + CE 9.1 => `https://jesus.speechmessage.com.tw/api/data/v9.1/`
   - never copies passwords

2. ChurchReport `DynamicsAccess` now includes:
   - Gateway / Embedded switch
   - profile/Web API values aligned from current `CrmConnection` cloud org (`jesus`)
   - secret reference only

3. Gateway `appsettings.json` aligned to the same jesus Web API root.

4. Bootstrap supports:
   - Package01 disabled => legacy ToolUtility
   - Gateway mode
   - Embedded mode via Embedded project (no product WebApi reference)

## Verification

- `dotnet test SpeechMessage.Dynamics.Tests` => 36 passed
- `dotnet build SpeechMessageProducts.ChurchReport` => success
- `dotnet build SpeechMessage.Dynamics.Gateway` => success
- `dotnet build SpeechMessage.Dynamics.ProductClient` => success

## Still deferred

- Live CRM fee-read smoke with Package01FeeReadsEnabled=true
- Durable multi-host coordinator
- Remove PowerPlatform.Dataverse.Client (Phase 6 only)