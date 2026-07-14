# B06C Performance And Design Analysis

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Findings

### B06C-PERF-001 Register performs multiple CRM lookups and writes synchronously on the request path

- Evidence:
  - `RegisterConnector.Register` calls `RetrieveContactCollectionByName` before entering the contact loop.
  - Each matched contact normalizes phones, calls `FindListCollection`, checks `DoesAccountExist`, and may call `UpdateEntity`.
  - `FindListCollection` calls `QueryListsAndOrderedByListName` for race leader and family leader paths.
- Impact: a single register attempt can perform contact search, list lookup, account lookup, and CRM update operations inline. Latency and CRM throttling risk increase with duplicate names or slow list queries.
- Status: confirmed design/performance risk; requires runtime timing to quantify.
- Recommended action: introduce a narrow register service contract that can batch or short-circuit CRM lookups, surface cancellation, and isolate the write path.
- Validation: measure CRM call count and latency for successful register, duplicate-account register, missing contact, and duplicate-name cases.

### B06C-PERF-002 `m_Lists.Entities.Count >= 0` makes the "no eligible list" branch unreachable

- Evidence:
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:118` checks `m_Lists.Entities.Count >= 0`.
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:153-155` has an `else` branch returning `沒有要點名的名單!`, but `Count >= 0` is always true.
- Impact: register eligibility based on race/family leader list membership may be bypassed for contacts whose mobile matches, weakening design intent and causing unnecessary account writes.
- Status: confirmed static logic issue.
- Recommended action: change the eligibility condition to require at least one qualifying list after preserving current behavior with tests.
- Validation: tests should cover a contact with matching mobile but no race/family leader list and prove no account/password update occurs unless the intended product rule allows it.

### B06C-PERF-003 Church hierarchy load is inherited through B06A/ListManagement and lacks B06C-specific gate proof

- Evidence:
  - `HomeController.ChurchRootRedirect` redirects `/Home/ChurchRoot` to `ListManagement.ChurchRoot`.
  - `ListManagementController.LoadChurchRoot` serves church hierarchy data through list/reference state.
  - The module map marks B06C dependent on B06A and gate-blocked because B06A-B06C have no directly attributable tests.
- Impact: B06C cannot safely optimize church hierarchy behavior until the B06A reference/list contract and B06C consumer expectations are proven.
- Status: confirmed boundary/gate risk.
- Recommended action: create a provider/consumer validation path for hierarchy shape, paging, and permissions before optimization.
- Validation: gate should verify `LoadChurchRoot` shape and route access from the B06C consumer perspective without pulling small-group reporting into B06C ownership.

## Runtime Measurement Needs

- CRM call count and elapsed time for each register outcome.
- Qualification read/write latency and CRM update behavior.
- Church hierarchy payload size and DataSourceLoader behavior under realistic hierarchy sizes.
