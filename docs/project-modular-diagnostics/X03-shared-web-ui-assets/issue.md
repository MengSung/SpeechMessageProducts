# X03 Shared Web UI Assets Diagnostic Issues

Status: DEGRADED_REVIEW_PENDING
Module: X03
Workspace: X03-shared-web-ui-assets
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 16f63c41d17cd0f4756e183abcc80e1db65f911bc0432246d52653e1da8c8f35

## Executive Summary

X03 has five confirmed shared-asset findings: broad eager DevExtreme loading,
duplicate/missing vendor roots, unaudited CDN and deployable backup/debug assets,
Razor tokens in static JavaScript, and collision-prone duplicated UI helpers. No
confirmed Critical issue, credential leak, or unmanaged-resource leak was found.

## Ranked Confirmed Issues

### X03-PERF-001 Shared layout eagerly loads broad optional DevExtreme payload

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 84
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 14
- Security urgency score: 4
- Performance gain score: 10
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: X03
- Cross-module: all shared-layout consumers
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:255
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:256
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:263
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:272
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:273
- Evidence: every shared-layout page loads `dx.all.js`, world/USA map data,
  Globalize/CLDR, and jszip regardless of page feature use; the vendor tree also
  contains very large all/debug bundles.
- Control/data/lifetime flow: shared layout render -> browser downloads/parses
  broad vendor bundle -> page initializes only a subset of features.
- Impact: transfer, parse, compile, and memory cost is paid across unrelated
  ChurchReport workflows.
- Why this is necessary: shared-layout placement makes this the highest-leverage
  X03 performance boundary rather than a page-local tuning issue.
- Recommended action: define an asset manifest and opt-in feature bundles for map,
  export, localization, and other optional DevExtreme capabilities.
- Validation: browser waterfall, JavaScript coverage, parsed bytes, and page-load
  budget on representative B-module workflows.
- Rollback boundary: feature-bundle manifest and layout includes; keep a legacy
  all-bundle switch until consumer smoke passes.
- Extraction contract: page feature declaration in; ordered canonical CSS/JS asset
  list out.
- CCG round history:
  - Round 1: run `20260711-181102-x03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### X03-SEC-001 CDN and deployable backup/debug assets lack supply-chain governance

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 20
- Impact score: 21
- Likelihood/frequency score: 12
- Security urgency score: 13
- Performance gain score: 4
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: X03
- Cross-module: X04B publish artifact audit
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:48
  - SpeechMessageProducts.ChurchReport/wwwroot/css/devextreme/dx-diagram.css.bak:1
  - SpeechMessageProducts.ChurchReport/wwwroot/js/devextreme/cldr.js.bak:1
- Evidence: the shared layout loads Font Awesome from cdnjs without SRI/local
  fallback, while deployable `wwwroot` contains numerous `.bak` and debug vendor
  assets.
- Control/data/lifetime flow: browser loads third-party CDN stylesheet and host
  publishes broad static vendor roots -> externally reachable asset surface.
- Impact: runtime supply-chain trust and unnecessary debug/backup exposure expand
  without an approved vendor/publish inventory.
- Why this is necessary: X03 owns asset selection while X04B needs a deterministic
  denylist/manifest to prove safe release artifacts.
- Recommended action: vendor or SRI-pin external assets, remove/exclude backup and
  debug-only files, and record approved versions/hashes.
- Validation: static publish inventory, denylist scan, and browser network check.
- Rollback boundary: asset source policy and publish exclusions; retain pinned local
  fallback during rollout.
- Extraction contract: approved vendor manifest and publish root in; verified asset
  inventory with hash/source/purpose out.
- CCG round history:
  - Round 1: run `20260711-181102-x03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### X03-PERF-002 Shared layout references duplicate and missing DevExtreme roots

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 20
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 4
- Performance gain score: 9
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: S
- Primary owner: X03
- Cross-module: X04B publish manifest and X01 host static files
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:255
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:260
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:261
  - SpeechMessageProducts.ChurchReport/Views/Shared/_Layout.cshtml:263
- Evidence: the layout mixes `~/js|css/devextreme` with
  `~/lib/devextreme/**`; the latter root was absent in the worktree scan while
  styles are also loaded from the former root.
- Control/data/lifetime flow: layout emits mixed asset URLs -> static-file
  middleware -> duplicate CSS evaluation or 404 responses in every consumer page.
- Impact: shared pages can incur failed requests and duplicate vendor-style work,
  with behavior dependent on deployment artifact contents.
- Why this is necessary: one canonical asset root is required before bundle budgets
  or browser baselines are reliable.
- Recommended action: select one canonical DevExtreme root and remove missing or
  duplicate references after browser/publish validation.
- Validation: browser network check for 404/duplicates plus publish manifest path
  assertions.
- Rollback boundary: shared layout asset references only.
- Extraction contract: canonical vendor root/version in; unique ordered asset URLs
  out.
- CCG round history:
  - Round 1: run `20260711-181102-x03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### X03-EXT-001 Static shared JavaScript embeds Razor-only route tokens

- Category: Extraction
- Severity: Medium
- Priority: P1
- Priority score: 76
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 13
- Security urgency score: 6
- Performance gain score: 6
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: X03
- Cross-module: B03 SmallGroupReport route consumer
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/wwwroot/js/Ajax.js:6
  - SpeechMessageProducts.ChurchReport/wwwroot/js/Ajax.js:34
- Evidence: static `wwwroot/js/Ajax.js` contains `@Url.Action` expressions for
  `SmallGroupReport`; static-file middleware does not Razor-render these tokens.
- Control/data/lifetime flow: browser downloads literal static JS -> AJAX helper
  sends requests to unresolved Razor-token strings and hard-coded business routes.
- Impact: route calls can fail and an X03 shared file remains coupled to one B03
  business controller and unsafe `GET` save semantics.
- Why this is necessary: endpoint configuration must be owned by consuming views;
  X03 should retain only route-neutral browser helpers.
- Recommended action: inject endpoints through `data-*` or a JSON configuration
  block and transfer business-specific behavior to B03.
- Validation: static scan for Razor tokens under `wwwroot/**/*.js` and browser smoke
  for current consumers.
- Rollback boundary: endpoint configuration and `Ajax.js` consumer include only.
- Extraction contract: consumer-supplied endpoint/method/selectors in; generic
  date/grid request helper out.
- CCG round history:
  - Round 1: run `20260711-181102-x03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

### X03-EXT-002 Shared UI helpers use global IDs and duplicated page includes

- Category: Extraction
- Severity: Medium
- Priority: P2
- Priority score: 68
- Confirmed: true
- Evidence confidence: 18
- Impact score: 16
- Likelihood/frequency score: 11
- Security urgency score: 4
- Performance gain score: 6
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: X03
- Cross-module: B03/B05/B06 UI consumers
- Gate blocked: true
- Files:
  - SpeechMessageProducts.ChurchReport/Views/Shared/_LoginLoadPanel.cshtml:8
  - SpeechMessageProducts.ChurchReport/Views/Home/SmallGroupReportView.cshtml:787
  - SpeechMessageProducts.ChurchReport/Views/Home/SmallGroupReportView.cshtml:790
  - SpeechMessageProducts.ChurchReport/Views/Home/FeeView.cshtml:460
  - SpeechMessageProducts.ChurchReport/Views/Home/FeeManagerView.cshtml:632
- Evidence: shared/page helpers rely on global `loadPanel` IDs and repeat common
  script includes; `SmallGroupReportView` includes `DataGridAjax.js` twice.
- Control/data/lifetime flow: views emit duplicate scripts/global IDs -> browser
  evaluates shared globals and initializes components by non-unique selectors.
- Impact: duplicate initialization, global collisions, and page-order dependency
  make shared behavior brittle across modules.
- Why this is necessary: stable component contracts reduce repeated per-page fixes
  and provide one browser-test seam.
- Recommended action: create namespaced/id-parameterized helpers and a deduplicated
  asset include contract for load panels, grids, toasts, and uploads.
- Validation: browser console, single-load assertions, and component initialization
  checks on representative workflows.
- Rollback boundary: migrate one helper and consumer page at a time.
- Extraction contract: unique component ID/options and declared assets in;
  initialized helper handle and deduplicated includes out.
- CCG round history:
  - Round 1: run `20260711-181102-x03-issue-review-r1-reviewer` returned
    `completedBackends=[]`; no reviewer finding was available.

## Runtime Validation Pending

- Browser waterfall, JavaScript coverage, 404/duplicate checks, and publish artifact
  inventory remain pending per `evidence/runtime-validation-plan.md`.

## Deleted Or Rejected Candidates

- No confirmed Critical X03 security issue, direct secret/token exposure, or
  unmanaged-resource leak was found in the static pass.

## Cross-Module Handoffs

- X04B owns publish artifact enforcement; X03 supplies the approved asset manifest.
- B03 owns SmallGroupReport endpoint behavior currently embedded in `Ajax.js`.

## Final CCG Approval

`DEGRADED_REVIEW_PENDING`; round 1 produced no usable backend output.
