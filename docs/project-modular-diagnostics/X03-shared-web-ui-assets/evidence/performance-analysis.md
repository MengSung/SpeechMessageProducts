# X03 Performance Analysis

Status: DEGRADED_REVIEW_PENDING
Module: X03
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Executive Finding

X03 has a confirmed front-end payload and static asset governance problem. The shared layout loads broad vendor bundles and duplicate/missing DevExtreme roots, while the repository stores large debug and backup assets under deployable `wwwroot` paths.

## Findings

### X03-PERF-001 Shared layout eagerly loads broad DevExtreme payload for every page

- Severity: High
- Status: Confirmed
- Evidence: `_Layout.cshtml` loads `~/js/devextreme/dx.all.js`, `~/js/devextreme/vectormap-data/world.js`, DevExtreme ASP.NET MVC glue, Globalize/CLDR chain, jszip, and `usa.js` in the shared layout.
- Evidence: `dx.all.js` is 5.4 MB on disk; `dx.all.debug.js` is 16.8 MB; related DevExtreme JS root totals 131.93 MB.
- Impact: Every page pays parse/download/cache-validation cost regardless of whether it uses maps, export, CLDR, or all DevExtreme widgets. This is the most valuable X03 optimization candidate.
- Validation needed: Browser waterfall and JS coverage against representative B-module workflows.

### X03-PERF-002 Layout references duplicate and missing DevExtreme roots

- Severity: High
- Status: Confirmed
- Evidence: `_Layout.cshtml` references both `~/css/devextreme/dx.common.css`/`dx.light.compact.css` and `~/lib/devextreme/css/dx.common.css`/`dx.spa.css`.
- Evidence: `wwwroot/lib/devextreme` is missing, while `wwwroot/css/devextreme` and `wwwroot/js/devextreme` exist.
- Impact: Pages can produce avoidable 404s and duplicate style evaluation. Missing static dependencies also make asset behavior environment-sensitive.
- Validation needed: Browser network check should confirm whether `~/lib/devextreme/**` returns 404 in the deployed host.

### X03-PERF-003 Deployable vendor tree contains `.bak`, debug, minified, and unminified variants together

- Severity: Medium
- Status: Confirmed
- Evidence: `wwwroot/css/devextreme` has 62.55 MB; `wwwroot/js/devextreme` has 131.93 MB; `.bak` and debug variants are stored beside runtime files.
- Impact: Repo/deploy size, backup scans, static file enumeration, and asset budget all grow. It also hides which file is canonical.
- Validation needed: Confirm publish output includes these files, then define a provider asset budget.

### X03-PERF-004 Common script inclusion is duplicated on several business pages

- Severity: Medium
- Status: Confirmed
- Evidence: `LessonList`, `NewPersonFollowUpView`, `MultiGroupView`, `PresentFeeListView`, and `SmallGroupReportView` include `DataGridAjax.js` twice; several pages repeat `SelectDate.js`, `DropDownBox.js`, and `LoadPanel.js` manually.
- Impact: Duplicate handlers or global functions can be overwritten unpredictably; page load order is fragile and difficult to budget.
- Validation needed: Browser console and coverage on representative views.

## Non-Findings

- No long-running timer, unmanaged resource, socket, or explicit memory leak was found in X03 owner files.
- The main performance risk is payload/load governance, not server-side loops.
