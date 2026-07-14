# X03 Scope Manifest

Status: DEGRADED_REVIEW_PENDING
Module: X03
Workspace: X03-shared-web-ui-assets
Mode: DIAGNOSIS_ONLY
Nested agent count: 0
Map source: ../module-boundaries-and-optimization-map.md

## Module Boundary

X03 owns shared Web UI and static asset platform responsibilities: shared layout/components, vendor CSS/JS, DevExtreme, Bootstrap, and cross-business frontend utilities. Business pages and single-business assets are explicitly excluded.

## Primary Owner Paths

- `SpeechMessageProducts.ChurchReport/Views/_ViewImports.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/_ViewStart.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/_LoadingPanelPartial.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/_LoadPanelComponent.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/_ToastComponents*.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/_UploadButtonPartial.cshtml`
- `SpeechMessageProducts.ChurchReport/wwwroot/lib/**`
- `SpeechMessageProducts.ChurchReport/wwwroot/assets/**`
- `SpeechMessageProducts.ChurchReport/wwwroot/css/devextreme/**`
- `SpeechMessageProducts.ChurchReport/wwwroot/js/devextreme/**`
- `SpeechMessageProducts.ChurchReport/wwwroot/js/Ajax.js`
- `SpeechMessageProducts.ChurchReport/wwwroot/js/DataGridAjax.js`
- `SpeechMessageProducts.ChurchReport/wwwroot/js/DropDownBox.js`
- `SpeechMessageProducts.ChurchReport/wwwroot/js/LoadPanel.js`
- `SpeechMessageProducts.ChurchReport/wwwroot/js/SelectDate.js`

## Explicit Exclusions

- Business pages under `Views/**` that consume common scripts or partials.
- Business-specific JavaScript such as `wwwroot/js/FeeDataGridAjax.js`.
- `wwwroot/js/TreeView.js`, currently assigned to X05Q until responsibility proof is complete.
- Product code, config, tests, generated output, `bin/**`, `obj/**`, cache output, and lockfiles for this diagnostic pass.

## Dependencies

- X01 host composition and static file serving.
- X04A configuration for theme selection used by `_Layout.cshtml`.
- DevExtreme, Bootstrap, jQuery, jszip, Globalize/CLDR, and Font Awesome assets.

## Consumers

- B01-B07 modules consume the shared layout/vendor asset contract.
- Observed consumers include `DedicationFeeView`, `PersonalReport`, `LessonList`, `Fee`, `FeeManagerView`, `Present`, `FeeView`, `IntegrateView`, `NewPersonFollowUpView`, `MultiGroupView`, `PresentFeeListView`, and `SmallGroupReportView`.

## Baseline Observations

- X03 has no complete provider gate yet; the map marks X03 as gate-blocked until browser/shared asset tests and asset budget are defined.
- Work is diagnosis-only. No source, config, test, generated, `bin`, `obj`, cache, or lockfile writes are permitted.

## Git Baseline

The worktree already contained many untracked project diagnostic artifacts before this X03 pass. This pass only writes the X03 diagnostic folder and x03-prefixed CCG artifacts.
