# B03 Extraction Analysis

Status: LOCAL_DIAGNOSIS_COMPLETE_CCG_PENDING
Module: B03
Mode: DIAGNOSIS_ONLY

## Finding: InMemoryDataContextSmallGroup Is A Cross-Module Session Container

Severity: High

Evidence:

- `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:53`
  through `InMemoryDataContextSmallGroup.cs:80` defines the B03 context and fields
  for memory cache, ToolUtility, ListManager, SmallGroupDataList, and
  WeeklyReportData.
- `InMemoryDataContextSmallGroup.cs:95` through
  `InMemoryDataContextSmallGroup.cs:132` adds HappyGroupDataManager,
  ListManagementDataManager, EquipmentDataManager, FeeList, LineBindingViewModel,
  AppointmentsListManager, DonationPaymentManager, and PollManager.
- `InMemoryDataContextSmallGroup.cs:906` through
  `InMemoryDataContextSmallGroup.cs:943` creates/caches
  `ListManagementDataManager`, which belongs to B06A by map responsibility.
- `InMemoryDataContextSmallGroup.cs:960` through
  `InMemoryDataContextSmallGroup.cs:998` creates/caches `EquipmentDataManager`,
  outside B03.
- `InMemoryDataContextSmallGroup.cs:1015` through
  `InMemoryDataContextSmallGroup.cs:1055` creates/caches `FeeList`, outside B03.
- `InMemoryDataContextSmallGroup.cs:1125` through
  `InMemoryDataContextSmallGroup.cs:1163` creates/caches
  `AppointmentsListManager`, outside B03.
- `InMemoryDataContextSmallGroup.cs:1180` through
  `InMemoryDataContextSmallGroup.cs:1222` creates/caches
  `DonationPaymentManager` with payment/LINE workflow dependencies, outside B03.
- `InMemoryDataContextSmallGroup.cs:1239` through
  `InMemoryDataContextSmallGroup.cs:1277` creates/caches `PollManager`.
- `InMemoryDataContextSmallGroup.cs:1300` through
  `InMemoryDataContextSmallGroup.cs:1333` creates/caches `ToolUtilityClass` via
  `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")`.
- `SpeechMessageProducts.ChurchReport/Models/ListManager.cs:25` through
  `ListManager.cs:53` keeps active list, account, password, multi-group data,
  and `ListSmallGroupWeeklyReport` in mutable fields.
- `ListManager.cs:220` through `ListManager.cs:244` mutates `ActiveListId`, binds
  `ListSmallGroupWeeklyReport`, and calls `DownloadIntegrateData.SetupIntegrateData`.

Assessment:

The file is B03-owned, but it currently acts as a session-scoped composition
root for B03 plus B02, B04, B05, B06, B07, F03A, and X02A concerns. That blocks
clean B03 extraction because module boundaries are implicit mutable session
properties rather than typed contracts.

Recommended next action:

Split the context by capability and owner. Create a narrow B03 session/report
state contract, move non-B03 managers to owner modules, expose B03 CRM work
through interfaces, and preserve compatibility properties until provider and
consumer gates pass.

## Rejected Candidates

- Bulk-moving all `WebServiceConnector` code: rejected because the map assigns
  only specific partials to B03.
- Treating X02A cache engine as B03: rejected; B03 owns small-group cache policy
  only.
- Treating QR utilities as B03: rejected; QR generation and controllers are B04C.
