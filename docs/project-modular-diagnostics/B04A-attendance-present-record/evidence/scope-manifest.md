# B04A Scope Manifest

Final status: DEGRADED_REVIEW_PENDING

## Module Identity

- Leaf ID: B04A
- Stable workspace: `B04A-attendance-present-record`
- Module name: Attendance and Present Record
- Primary responsibility: attendance record download/upload, present-record service contract, present-record DTO/mapping/test ownership.
- Explicit exclusions: appointment, equipment, scheduling, QR, generic CRM API, and fee master data.
- Primary dependencies: F03A CRM access, B01 identity/session/access control, B02 contact/member identity.
- Consumer: B04C scheduling/QR integration depends on the attendance contract.

## Primary Owner Paths

- `SpeechMessageProducts.ChurchReport/Services/PresentRecord/**`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs`
- `SpeechMessageProducts.ChurchReport/Views/Home/PresentFeeListView.cshtml`
- Present-record-only DTO, mapping, and tests when they exist.

## Direct Caller / Boundary Evidence

- `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs` exposes insert, update, and delete actions for present records.
- `SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` calls `IPresentRecordService.CreatePresentRecordAsync`, but no concrete implementation was found under `Services/PresentRecord/**`.
- Legacy present-record count logic appears duplicated across `DownloadIntegrateData.Identity.cs`, `UploadIntegrateData.Contact.cs`, `UploadData.cs`, `PersonalInfomatioManager.cs`, and `NewPerson.cs`; these are consumers or legacy extraction candidates, not all B04A-owned for this isolated diagnostic.

## Existing Diagnostic Directory State

- Before this pass, the target diagnostic directory existed with only `evidence/`.
- This pass creates the required seven diagnostic files under the allowed B04A documentation scope.

## Gate / Test Status

- The module map states B03, B04A-B04C, and B06A-B06C have no directly attributable existing test suite.
- Provider gate expected by the map: Attendance tests.
- Consumer gate expected by the map: B04C scheduler/QR integration.
- This diagnostic does not claim optimization completion because the current scope is analysis-only and gate coverage is missing.

## Write Scope Compliance

- Allowed documentation scope: `docs/project-modular-diagnostics/B04A-attendance-present-record/**`.
- Allowed CCG scope: `.ccg/dual-model-runs/**` files and run folders with `b04a` / `B04A` prefix.
- Product code, config, tests, generated files, bin/obj/cache, lockfiles, and ledger were not modified by this documentation write.
