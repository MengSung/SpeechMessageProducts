# X03 Extraction Analysis

Status: DEGRADED_REVIEW_PENDING
Module: X03
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Executive Finding

X03 can be accelerated by extracting a clean shared asset contract and turning ad hoc shared JavaScript into a small endpoint-configured module. The current files mix platform concerns, demo/vendor leftovers, business-specific endpoints, and duplicated page includes.

## Extraction Candidates

### Candidate 1: Shared Asset Manifest / Bundle Contract

- Status: Recommended
- Owning files: `_Layout.cshtml`, `wwwroot/css/devextreme/**`, `wwwroot/js/devextreme/**`, `wwwroot/lib/**`, `wwwroot/assets/**`.
- Clean contract: one manifest that declares required global assets, optional feature bundles, and page-level feature flags such as DevExtreme grid, map, export, and localization.
- Why valuable: Highest payoff. It provides one place to remove missing `~/lib/devextreme/**` references, stop loading map/export/localization assets globally, and enforce an asset budget.
- Risk: Requires browser workflow coverage for affected B modules.

### Candidate 2: Shared DevExtreme UI Helpers

- Status: Recommended
- Owning files: `_LoadingPanelPartial.cshtml`, `_LoadPanelComponent.cshtml`, `_ToastComponents*.cshtml`, `_UploadButtonPartial.cshtml`, `LoadPanel.js`.
- Clean contract: stable component IDs, helper APIs for show/hide/notify, and configurable button action names without duplicate hardcoded IDs.
- Why valuable: Reduces repeated partial patterns and prevents global ID collisions such as `loadPanel` being defined by multiple partials.
- Risk: Must audit consuming pages for ID assumptions before implementation.

### Candidate 3: Endpoint-Configured Common Grid/Date Scripts

- Status: Recommended with rewrite
- Owning files: `Ajax.js`, `DataGridAjax.js`, `DropDownBox.js`, `SelectDate.js`.
- Clean contract: no Razor inside static JS; consuming views provide endpoint URLs and selectors through `data-*` attributes or a small JSON config block.
- Why valuable: Fixes broken static Razor tokens and allows tests/browser checks to validate route behavior.
- Risk: Some functions appear business-specific to `SmallGroupReport` and may need transfer to B03/X05Q after responsibility proof.

## Looping / Automation Opportunities

- Generate an asset inventory report from `wwwroot` with file counts, `.bak` counts, debug counts, and top file sizes.
- Add a browser-network script that fails on 404 static assets, duplicate critical vendor loads, and payload budget regression.
- Add a simple static scan for Razor tokens under `wwwroot/**/*.js`.
- Batch-check business views for repeated script includes.

## Rejected Candidates

- Moving all `wwwroot/js` into X03: rejected. The module map explicitly keeps business-specific assets with their business modules and assigns `TreeView.js` to X05Q pending proof.
- Optimizing all DevExtreme files immediately: rejected for this pass because X03 is gate-blocked and this task is diagnosis-only.
