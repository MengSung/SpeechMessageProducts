# B06C Scope Manifest

Module: B06C
Workspace: `docs/project-modular-diagnostics/B06C-church-hierarchy-register/`
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Boundary Source

- Map row: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`, B06C row.
- Module name: Church hierarchy and Register.
- Primary responsibility: church hierarchy, register, qualification, and related reference flow.
- Explicit exclusions: small-group reporting, fee transactions, and unrelated member/payment flows except as dependency or consumer context.
- Gate state: blocked. The module map states B03, B04A-B04C, and B06A-B06C do not have directly attributable existing test suites.

## Primary Owner Files

- `SpeechMessageProducts.ChurchReport/Models/RegisterManager.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs`
- `SpeechMessageProducts.ChurchReport/Views/Home/Register.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/QualificationView.cshtml`
- `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs`, B06C slices:
  - `/Home/QualificationView/{lineIdLoginViewParameter}`.
  - `/Home/GetQualificationData`.
  - `/Home/SaveQualificationData`.
  - legacy `/Home/ChurchRoot` redirect.
- `SpeechMessageProducts.ChurchReport/Controllers/ListManagementController.cs`, B06C consumer/context slices:
  - `ChurchRoot`.
  - `LoadChurchRoot`.
- `SpeechMessageProducts.ChurchReport/ViewModels/GalleryViewModel.cs`, B06C-related view models:
  - `RegisterViewModel`.
  - `LineBindingViewModel` qualification fields and CRM helpers.

## Dependency Context

- F03A CRM operations: B06C register and qualification flows call ToolUtility/CRM helpers and mutate contact records.
- B01 identity/session: qualification and register routes must be protected by the host authorization/session model.
- B02 member/contact profile: qualification data is stored against CRM contact identity via LINE user id.
- B06A reference/list data: church hierarchy and qualification option/reference data depend on list/reference surfaces.
- X03 shared web UI assets: B06C views use jQuery, unobtrusive AJAX, DevExtreme controls, and LIFF client scripts.

## Consumer Context

- Small-group reporting is excluded from B06C, but B06C register currently proves eligibility by finding race/family leader lists before setting application credentials.
- Fee/payment flows are excluded from B06C.
- Member/contact flows are excluded except where qualification fields and LINE user identity are direct dependencies or consumers.

## Evidence Commands

- Read workflow: `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`.
- Read map row and B06C section from `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`.
- Static search used `rg` over owner symbols: `RegisterManager`, `RegisterConnector`, `RegisterViewModel`, `QualificationView`, `GetQualificationData`, `SaveQualificationData`, `ChurchRoot`, and `LoadChurchRoot`.
- No restore, build, test, package restore, code generation, formatting, or migrations were run during local diagnostic evidence gathering.
