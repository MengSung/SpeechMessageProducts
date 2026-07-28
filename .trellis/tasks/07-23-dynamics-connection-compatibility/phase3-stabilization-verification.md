# Phase 3 Stabilization Verification

Date: 2026-07-25  
Checkpoint: stabilize Package 1 read wiring before more features

## Commands run

All commands used `--no-restore` because sandbox cannot read `C:\Users\Administrator\AppData\Roaming\NuGet\NuGet.Config`. Project assets were already present under each project's `obj/`.

| Check | Result |
| --- | --- |
| `dotnet test SpeechMessage.Dynamics.Tests ... --no-restore` | **PASS** 41 / 41 |
| `dotnet test SpeechMessage.Dynamics.SmokeTests ... --no-restore` | **PASS** 4 / 4 (live CRM disabled by default) |
| `dotnet build SpeechMessage.Dynamics.Gateway ... --no-restore` | **PASS** 0 warning / 0 error |
| `dotnet build SpeechMessageProducts.ChurchReport ... --no-restore` | **PASS** (pre-existing NU1903 warnings on ToolUtility / PowerPlatform.Dataverse.Client only) |

## Boundary audit

| Rule | Evidence |
| --- | --- |
| ChurchReport direct refs only Abstractions + ProductClient + Embedded | `SpeechMessageProducts.ChurchReport.csproj` ProjectReferences confirmed |
| ChurchReport has no direct ProjectReference to WebApi | grep for `SpeechMessage.Dynamics.WebApi` on product csproj: none |
| WebApi remains private to Gateway / Embedded / tests / smoke | project descriptions + references |
| `PowerPlatform.Dataverse.Client` still present | retained until Phase 6 (still pulled via ToolUtility / ChurchReport legacy) |
| Feature flag default off | `DynamicsAccess:Package01FeeReadsEnabled = false` in ChurchReport appsettings |

## Docs produced / updated this checkpoint

- `phase3-enablement-rollback.md` — enable tiers, rollback, covered/not covered
- `phase3-package1-consumer-matrix.md` — added safe enable tiers
- this file — verification evidence

## Explicitly NOT done in this checkpoint

- No write-path migration
- No SDK / PowerPlatform.Dataverse.Client deletion
- No production enable of Package01FeeReads
- No live WhoAmI on this host (network/credential path to jesus not assumed available here)
- CCG dual-model review deferred if provider quota blocks runner

## Next after this gate

1. Non-prod Tier A enable only, with operator rollback ready
2. Live smoke on a credentialed host that can reach Web API
3. Then Package 0 option-set product wiring / write-path design
4. Phase 6 SDK removal only after all consumers leave legacy
