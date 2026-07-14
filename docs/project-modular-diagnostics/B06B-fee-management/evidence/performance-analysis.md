# B06B Performance And Design Analysis

## Static Findings

### P1 - Full-list materialization before DevExtreme shaping

- `GetLessons` loads `InMemoryContext.FeeList.LessonList` and then calls `DataSourceLoader.Load`.
- `GetFeeData` loads `InMemoryContext.FeeList.FeeDataList` and then calls `DataSourceLoader.Load`.
- `FeeList.SetupLessonList`, `SetupPresentFeeList`, and `SetupFeeDataList` delegate to `FeeDownUpLoader` before the DataSourceLoader phase.

Risk: DataSourceLoader shapes already-materialized lists, so large CRM result sets can create request latency and memory pressure. Runtime profiling should measure lesson/present-fee list size and request timing before any rewrite.

### P2 - Duplicated active and legacy UI surfaces

- Active B06B UI exists under `Views/FeeManagement/LessonList.cshtml`, `Fee.cshtml`, and `Present.cshtml`.
- Legacy or compatibility views/actions remain under `Views/Home/FeeView.cshtml`, `Views/Home/PresentFeeListView.cshtml`, `Views/Home/FeeManagerView.cshtml`, and `HomeController` redirect methods.
- Some views reference `FeeDataGridAjax.js`, while active Fee/Present views also include inline save/pending-change behavior.

Risk: duplicated UI surfaces increase validation cost and can cause inconsistent endpoint usage during extraction.

### P3 - Large mutable ViewBag contract for column headers

- `SetFeeManagerViewBag` maps many `m_ClassName` fields into `ViewBag.Colume*` entries.
- Fee and present views depend on dynamic column/header state derived from CRM-backed `ClassName`.

Risk: dynamic ViewBag contracts are hard to test and make it difficult to define a stable B06B view model or service contract.

## Acceleration Candidates

- Add route-level smoke coverage for the active FeeManagement views before optimizing.
- Add representative data-size measurements for `LessonList` and `FeeDataList`.
- Separate server-side read model shaping from DevExtreme/UI shaping once runtime hotspots are proven.
