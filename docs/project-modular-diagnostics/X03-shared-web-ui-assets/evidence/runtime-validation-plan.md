# X03 Runtime Validation Plan

Status: DEGRADED_REVIEW_PENDING
Module: X03
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Purpose

Validate X03 findings before optimization. This plan intentionally does not modify product code in the diagnostic pass.

## Provider Gate Candidates

1. Static asset inventory gate
   - Count files and bytes under X03-owned asset roots.
   - Fail if `.bak` or debug vendor files are included in publish output without explicit approval.
   - Record largest assets and total DevExtreme payload.

2. Shared layout network gate
   - Start the ChurchReport host in a controlled environment.
   - Load a representative page using `_Layout.cshtml`.
   - Fail on static asset 404s, especially `~/lib/devextreme/**` references.
   - Capture total JS/CSS transfer size and request count.

3. Static JS contract gate
   - Scan `wwwroot/**/*.js` for Razor tokens such as `@Url.Action`.
   - Fail when static JS embeds server-only Razor expressions.

4. Duplicate include gate
   - Scan Razor views for repeated identical `<script src="~/js/...">` includes in the same view.
   - Report duplicate common script includes for owner triage.

## Consumer Gate Candidates

- B02/B03/B04B/B04C/B06A/B06B representative browser workflows that use the shared layout and DevExtreme widgets.
- At minimum, include one grid-heavy workflow and one upload/toast/load-panel workflow.
- Verify console has no JS errors, load panels/toasts still render, and DevExtreme widgets initialize.

## Runtime Validation Pending Items

- Confirm whether `wwwroot/lib/devextreme/**` is genuinely missing at runtime or supplied by another deployment path.
- Confirm whether `.bak` files are included in published static output.
- Confirm actual browser payload cost after server compression and caching.
- Confirm whether `Ajax.js` is actively loaded by any live route; if not, candidate can shift from rewrite to deletion.

## Suggested Commands / Checks

These are future validation candidates, not executed during this diagnosis-only pass:

```powershell
rg -n "@Url\.Action|@Html|@\{" SpeechMessageProducts.ChurchReport/wwwroot -g "*.js"
rg -n "<script src=\"~/js/(Ajax|DataGridAjax|DropDownBox|LoadPanel|SelectDate)\.js\"" SpeechMessageProducts.ChurchReport/Views
```
