# Phase 0 verification log

## 2026-07-24 no-SDK removal gate update

### Scope verified

- Phase 0 machine-readable schema exists and is valid JSON.
- Phase 0 report-only matrix exists and is valid JSON.
- The matrix contains SDK-001 through SDK-007.
- SDK-006 records SpeechMessageProducts.sln including PowerPlatform.Dataverse.Client as a buildable project.
- SDK-007 records ToolUtility/ToolUtility.csproj referencing PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj.
- Phase 0 report-only no-SDK scanner exists under `eng/` and runs on PowerShell 5.1 without requiring .NET Core-only path APIs.
- The scanner excludes build output directories (`bin` / `obj`) and scans source, solution, project, package, props, targets, config, and JSON artifacts under the declared source roots.
- The scanner is intentionally report-only in Phase 0; it produces visibility but does not fail CI until later migration phases promote it to a failing gate.
- Mandatory-new-SpeechMessage.Dynamics.sln wording scan returned no matches for the rejected patterns.
- Final no-SDK removal wording is present in PRD, design, implementation plan, inventory, matrix, and main design spec.

### Commands run

~~~powershell
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json -Raw | ConvertFrom-Json | Out-Null
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json -Raw | ConvertFrom-Json | Out-Null
rg -n "Build a new, standalone solution|Create SpeechMessage\.Dynamics\.sln|dotnet build SpeechMessage\.Dynamics\.sln|dotnet test SpeechMessage\.Dynamics\.sln|mandatory separate \*\*SpeechMessage\.Dynamics\.sln\*\*" .trellis/tasks/07-23-dynamics-connection-compatibility docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md --glob "!phase0-verification.md"
rg -n "PowerPlatform.Dataverse.Client.*(temporary legacy|remove|removing|removed|deleting|buildable source)|ProjectReference|solution-project-inclusion|SDK-006|SDK-007|final SDK-removal gate|Final no-SDK acceptance" .trellis/tasks/07-23-dynamics-connection-compatibility .ccg/tasks/dynamics-connection-compatibility/task.json docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\eng\Verify-NoDynamicsSdk.ps1" -ManifestPath ".\eng\no-sdk-source-roots.json" -Json
git diff --check -- .trellis/tasks/07-23-dynamics-connection-compatibility .ccg/tasks/dynamics-connection-compatibility/task.json docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md eng .github/workflows/toolutility-tests.yml
~~~

### Results

- JSON validity: passed.
- Mandatory separate solution stale wording: no rejected wording found.
- Final no-SDK removal wording: found across expected artifacts.
- Report-only no-SDK scanner: passed with exit code 0 in `report-only` mode and found 1072 current source/project SDK references after excluding `bin` / `obj`.
- Scanner build-output exclusion check: 0 findings under `bin` / `obj`.
- CI scanner step uses Windows PowerShell-compatible `shell: powershell` and `-SummaryOnly` so Phase 0 logs show rule counts without dumping every legacy line.
- CCG review `20260724-173140-phase0-no-sdk-scanner-report-only-reviewer`: full dual-model success, no Critical findings; warnings addressed for CI shell, UTF-8 scanning, and log volume.
- Scanner known-path coverage:
  - `SpeechMessageProducts.sln`: hit `PowerPlatform.Dataverse.Client` solution inclusion (`SDK-006` evidence).
  - `ToolUtility/ToolUtility.csproj`: hit `PowerPlatform.Dataverse.Client` ProjectReference (`SDK-007` evidence).
  - `ToolUtility.Tests/ToolUtility.Tests.csproj`: hit `Microsoft.CrmSdk.CoreAssemblies` (`SDK-001` evidence).
  - `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`: hit legacy borrowed connector / Microsoft Dataverse SDK patterns (`SDK-002` / `SDK-003` evidence).
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj`: hit Microsoft Dataverse SDK package and `Microsoft.Crm.Sdk.Proxy.dll` HintPath (`SDK-004` / `SDK-005` evidence).
- Scanner rule distribution:
  - `SDKNS001` / `Microsoft.Xrm`: 625
  - `SDKTYPE001` / `IOrganizationService`: 269
  - `LEGACYPROJECT001` / `PowerPlatform.Dataverse.Client`: 89
  - `SDKTYPE002` / `OrganizationServiceProxy`: 41
  - `SDKASM001` / `Microsoft.Crm.Sdk`: 37
  - `SDKPKG001` / `Microsoft.PowerPlatform.Dataverse`: 7
  - `SDKPKG002` / `Microsoft.CrmSdk`: 2
  - `SDKTYPE003` / `DiscoveryServiceProxy`: 1
  - `SDKPATH001` / `Dynamics 365 SDK DLL`: 1
- Diff whitespace check: passed.
- UTF-8 no BOM / CRLF check: passed across the Phase 0 task artifacts, scanner files, design spec, and workflow file touched by this update.

### Remaining Phase 0 work

- Continue normalizing high-signal legacy call sites into normalizedCallSites rows.
- Select the first bounded read-heavy ChurchReport or ToolUtility use case only after matrix evidence is sufficient.
- Before promoting this scanner to a failing gate, add an audited baseline / false-positive policy and exact DLL filename/path checks for direct SDK binaries.

## 2026-07-25 normalizedCallSites first batch

### Scope completed

- Added evidence-backed `normalizedCallSites` rows `ORG-CALL-00001` through `ORG-CALL-00016`.
- Source evidence came from ChurchReport Startup DI pool registration, ToolUtility connection factory/pool WhoAmI validation, FeeService `new_fee` FetchXML reads, ToolUtility generic entity CRUD wrappers, ListService membership actions, and ChurchReport TimedOrganizationService decorator.
- High-signal candidate triage updated:
  - `CAND-001`..`CAND-003`, `CAND-005`, `CAND-006` -> `normalized`
  - `CAND-004` remains `needs-member-level-normalization` for remaining dynamic-list retrieve members
  - `CAND-007` added for `ToolUtility/ListOperations/ListService.cs` (membership rows present; retrieve members still open)
- Source candidate groups `SRC-GRP-001` and `SRC-GRP-002` moved to `normalization-in-progress`.
- Generic entity CRUD rows are intentionally `temporary-legacy` with `*.blocked` capability IDs so they cannot be mistaken for final registry operations.
- First bounded product use-case recommendation remains fee read path: `ORG-CALL-00005` + `ORG-CALL-00006`. Secondary list catalog reads: `ORG-CALL-00014`..`ORG-CALL-00016`.

### Validation commands

~~~powershell
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json -Raw | ConvertFrom-Json | Out-Null
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json -Raw | ConvertFrom-Json | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\eng\Verify-NoDynamicsSdk.ps1" -ManifestPath ".\eng\no-sdk-source-roots.json" -Json
git diff --check -- .trellis/tasks/07-23-dynamics-connection-compatibility eng .github/workflows/toolutility-tests.yml
~~~

### Remaining Phase 0 work

- Continue member-level normalization for remaining high-signal list retrieve helpers and additional ChurchReport/ToolUtility production call sites.
- Keep scanner in report-only mode; do not promote to failing gate yet.
- Do not create new Dynamics projects or delete `PowerPlatform.Dataverse.Client` in Phase 0.
- Select first bounded read-heavy fee use case only after version/smoke evidence rows advance beyond metadata-only.

## 2026-07-25 dual-model review status

- Local validation completed for schema/matrix parse, enum/id checks, report-only no-SDK scanner, and `git diff --check`.
- External CCG dual-model reviewer was prepared at `.ccg/dual-model-runs/phase0-normalized-callsites-review-input.md` but not executed in this turn because the unsandboxed external-model run requires explicit user approval for repository content egress.
- Proceeding with local verification only until that approval is granted.

## 2026-07-25 normalizedCallSites second batch

### Scope completed

- Expanded `normalizedCallSites` from 16 to 25 (`ORG-CALL-00017` through `ORG-CALL-00025`).
- Covered remaining ListService static/dynamic membership reads and reverse membership:
  - `ORG-CALL-00017` static `listmember` by listId
  - `ORG-CALL-00018` dynamic stored `list.query` FetchXML execution (temporary-legacy / high risk)
  - `ORG-CALL-00019` list membership by contactId
  - `ORG-CALL-00020` list by name
- Covered first ChurchReport product package rows:
  - `ORG-CALL-00021` BaseChurchController pool borrow/return
  - `ORG-CALL-00022` MemberInfo ungrouped contact page read
  - `ORG-CALL-00023` MemberInfo LINE profile contact update
  - `ORG-CALL-00024` MemberInfo commitment-type count function
  - `ORG-CALL-00025` MemberInfo list-name batch by contactIds
- High-signal triage:
  - `CAND-004` / `CAND-007` -> `normalized`
  - `CAND-008` BaseChurchController added and normalized
  - `CAND-009` MemberInfoController added; still `needs-member-level-normalization` because many helper RetrieveMultiple paths remain
- Important risk note: dynamic list FetchXML execution and controller-held `IOrganizationService` remain temporary migration debt, not final public gateway surfaces.

### Validation

~~~powershell
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json -Raw | ConvertFrom-Json | Out-Null
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json -Raw | ConvertFrom-Json | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\eng\Verify-NoDynamicsSdk.ps1" -ManifestPath ".\eng\no-sdk-source-roots.json" -SummaryOnly
git diff --check -- .trellis/tasks/07-23-dynamics-connection-compatibility .ccg/tasks/dynamics-connection-compatibility/task.json
~~~

### Remaining Phase 0 work

- Continue MemberInfo helper normalization and other ChurchReport controllers (Fee/Dedication/ListManagement/NewPerson).
- Keep no-SDK scanner report-only.
- Do not implement new Dynamics projects or remove PowerPlatform.Dataverse.Client in Phase 0.
- First bounded migration package candidates remain:
  1. fee reads `ORG-CALL-00005` / `ORG-CALL-00006`
  2. MemberInfo ungrouped page + LINE profile update `ORG-CALL-00022` / `ORG-CALL-00023`

## 2026-07-25 normalizedCallSites third batch

### Scope completed

- Expanded `normalizedCallSites` from 25 to 40 (`ORG-CALL-00026` through `ORG-CALL-00040`).
- MemberInfo package deepened:
  - present records, stor lessons, contact image read/write, basic info update
  - small-group descriptors/memberships, connection relation goals
- Product CRM edges outside thin controllers were recorded:
  - NewPerson image update
  - ListManagementDataManager field updates (temporary-legacy, needs field split)
  - DonationFeePaymentProcessor / RecurringDonationPaymentProcessor financial writes
  - DonationContactService open create/update bag (temporary-legacy)
  - AppointmentsDownUpLoader create/update/assign
  - OptionSetMetadataService metadata reads
- High-signal candidates now include CAND-010..CAND-016.
- Important architectural finding reinforced: many controllers are thin; real SDK edges live in Models/Services/Tools/WebServiceConnector.

### Validation

~~~powershell
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json -Raw | ConvertFrom-Json | Out-Null
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json -Raw | ConvertFrom-Json | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\eng\Verify-NoDynamicsSdk.ps1" -ManifestPath ".\eng\no-sdk-source-roots.json" -SummaryOnly
git diff --check -- .trellis/tasks/07-23-dynamics-connection-compatibility .ccg/tasks/dynamics-connection-compatibility/task.json
~~~

### Remaining Phase 0 work

- Continue manager/helper member-level normalization for FeeList, DonationBookingService, ContactService, DownloadListManager, and remaining MemberInfo paging helpers.
- Keep scanner report-only; no Dynamics project implementation or PowerPlatform.Dataverse.Client deletion in Phase 0.
- First migration package candidates remain fee reads + MemberInfo basic read/write + payment completion writes after evidence advances.

## 2026-07-25 normalizedCallSites fourth batch

### Scope completed

- Expanded `normalizedCallSites` from 40 to 50 (`ORG-CALL-00041` through `ORG-CALL-00050`).
- Donation / fee / new-person / list-download edges added:
  - dedication booking read/cancel
  - named contact create with dedication numbering
  - new-person full onboarding orchestration
  - contact assign-owner + generic AssignRequest temporary-legacy
  - list member count via DownloadListManager
  - FeeDownUpLoader dual-entity fee editor writes
  - sensitive contact card-profile (`new_visa_info`) update
  - FeeList in-memory staging clarified as non-CRM edge
- High-signal candidates now include CAND-017..CAND-024.
- Security note: `ORG-CALL-00049` is credential/token material and requires security-audit + no logging of visaInfo.

### Validation

~~~powershell
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json -Raw | ConvertFrom-Json | Out-Null
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json -Raw | ConvertFrom-Json | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\eng\Verify-NoDynamicsSdk.ps1" -ManifestPath ".\eng\no-sdk-source-roots.json" -SummaryOnly
git diff --check -- .trellis/tasks/07-23-dynamics-connection-compatibility .ccg/tasks/dynamics-connection-compatibility/task.json
~~~

### Remaining Phase 0 work

- Continue ContactService transfer helpers, DownloadListManager full download path, FeeDownUpLoader remaining key branches, and donation form services.
- Keep scanner report-only; no Dynamics project implementation or PowerPlatform.Dataverse.Client deletion in Phase 0.

## 2026-07-25 normalizedCallSites fifth batch

### Scope completed

- Expanded `normalizedCallSites` from 50 to 60 (`ORG-CALL-00051` through `ORG-CALL-00060`).
- Added transfer/download/auth/query edges:
  - ContactService list transfer orchestration and current-group resolve
  - DownloadListManager login+user lists and role-based list queries
  - ToolUtility account/LINE contact auth lookups
  - many-to-many app-named membership query
  - open `associationName` list query temporary-legacy risk
  - dedication booking FetchXML implementation
  - dedication fee form contact resolve
- Security notes:
  - `ORG-CALL-00055` plaintext `new_app_pass` comparison is temporary-legacy credential risk
  - `ORG-CALL-00058` must not remain a free-form attribute-name API
- High-signal candidates now include CAND-025..CAND-029.

### Validation

~~~powershell
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json -Raw | ConvertFrom-Json | Out-Null
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json -Raw | ConvertFrom-Json | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\eng\Verify-NoDynamicsSdk.ps1" -ManifestPath ".\eng\no-sdk-source-roots.json" -SummaryOnly
git diff --check -- .trellis/tasks/07-23-dynamics-connection-compatibility .ccg/tasks/dynamics-connection-compatibility/task.json
~~~

### Remaining Phase 0 work

- Locate concrete IListManagementService / IPresentRecordService implementations and normalize their CRM edges.
- Continue FeeDownUpLoader remaining key branches and FetchXmlQueryService other query methods.
- Keep scanner report-only; no Dynamics project implementation or PowerPlatform.Dataverse.Client deletion in Phase 0.

## 2026-07-25 normalizedCallSites sixth batch

### Scope completed

- Expanded `normalizedCallSites` from 60 to 70 (`ORG-CALL-00061` through `ORG-CALL-00070`).
- FetchXmlQueryService core methods covered: stor lessons, disciple-lesson stor lessons, meeting stats, fee-by-dedication-period, app-named list catalogs.
- FeeDownUpLoader load/create paths covered (`ORG-CALL-00066` / `00067`) in addition to earlier update path.
- Present-record concrete edges found in integrate download/upload files rather than `IPresentRecordService` implementation classes.
- Important Phase 0 finding: `IListManagementService` / `IPresentRecordService` are interface-only in this repository snapshot (`ORG-CALL-00070` + CAND-032/033). ContactService depends on them, but concrete CRM membership/present-record behavior currently lives in ToolUtility list helpers and WebServiceConnector integrate data files.
- Upload present-record path remains temporary-legacy because of attribute-bag create retries.

### Validation

~~~powershell
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json -Raw | ConvertFrom-Json | Out-Null
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json -Raw | ConvertFrom-Json | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\eng\Verify-NoDynamicsSdk.ps1" -ManifestPath ".\eng\no-sdk-source-roots.json" -SummaryOnly
git diff --check -- .trellis/tasks/07-23-dynamics-connection-compatibility .ccg/tasks/dynamics-connection-compatibility/task.json
~~~

### Remaining Phase 0 work

- Search outside current roots or DI composition for any missing list/present service implementations if they exist in untracked/generated code.
- Continue branch-level normalization only where it adds new entity/operation shapes.
- Consider Phase 0 readiness review for first migration package selection once high-traffic packages stabilize.
- Keep scanner report-only; no Dynamics project implementation or PowerPlatform.Dataverse.Client deletion in Phase 0.

## 2026-07-25 migration package selection

### Scope completed

- Stopped unbounded call-site expansion at 70 normalized rows.
- Added Phase 0 readiness / first-package selection artifact:
  - `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-migration-package-selection.md`
- Recommended order:
  1. Package 0 runtime foundation
  2. Package 1 fee reads (first business package)
  3. Package 2 MemberInfo basic read/write
  4. Later list / present-record / payments / new-person packages
- Explicit exclusions for first business package: fee writes, payment completion, card profile, dynamic FetchXML, generic CRUD, plaintext app-password auth.
- Phase 0 remains planning-only; no Dynamics project implementation and no PowerPlatform.Dataverse.Client deletion.

### Validation

~~~powershell
Get-Content .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json -Raw | ConvertFrom-Json | Out-Null
Test-Path .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-migration-package-selection.md
git diff --check -- .trellis/tasks/07-23-dynamics-connection-compatibility .ccg/tasks/dynamics-connection-compatibility/task.json
~~~

### Remaining Phase 0 work

- Owner accept Package 0 + Package 1 selection.
- Optional dual-model review of package selection.
- Freeze CE 8.2/9.1 profile config shape if any open design wording remains.
- Only then proceed to Phase 1 scaffolding in SpeechMessageProducts.sln.

## 2026-07-25 phase1 scaffolding started

### Owner acceptance

- Package 0 + Package 1 selection accepted.
- Phase 1 scaffolding started in existing `SpeechMessageProducts.sln`.

### Projects added

- SpeechMessage.Dynamics.Abstractions
- SpeechMessage.Dynamics.WebApi
- SpeechMessage.Dynamics.Embedded
- SpeechMessage.Dynamics.Gateway
- SpeechMessage.Dynamics.Tests
- SpeechMessage.Dynamics.SmokeTests

### Package 0/1 scope in scaffolding

- Operation registry for runtime health/metadata and fee-read operations
- Controlled executor rejecting unknown operations/parameters
- Gateway HTTP route skeleton: `POST /v1/organizations/{alias}/operations/{capabilityOperationId}`
- Embedded DI entrypoint
- Boundary test: products must not reference WebApi

### Still not done

- Live CE 8.2/9.1 HTTP execution
- Secret store integration
- Admission/capacity runtime
- ChurchReport consumer switch
- PowerPlatform.Dataverse.Client removal
