# B06B Scope Manifest

## Boundary Map Row

- Leaf ID: `B06B`
- Module: Fee management
- Primary responsibility: `FeeManagement`, fee/lesson/present fee master data and UI
- Explicit exclusions: donation payment transactions and provider callbacks
- Primary dependencies: F03A CRM operations, B01 identity/session, B06A list/reference data, X03 shared web UI assets
- Primary consumer: B05 donation payment flow through the Fee master data contract

## Primary Owner Files

- `SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs`
- `SpeechMessageProducts.ChurchReport/Models/Fee.cs`
- `SpeechMessageProducts.ChurchReport/Models/FeeList.cs`
- `SpeechMessageProducts.ChurchReport/Views/FeeManagement/Fee.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/FeeManagement/LessonList.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/FeeManagement/Present.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/FeeManagerView.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/FeeView.cshtml`
- `SpeechMessageProducts.ChurchReport/wwwroot/js/FeeDataGridAjax.js`

## Additional Compatibility Context

- `SpeechMessageProducts.ChurchReport/Views/Home/PresentFeeListView.cshtml`
- `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs` legacy redirect actions for old fee-management routes

These paths are relevant to route compatibility and validation. `PresentFeeListView.cshtml` is not listed as a primary owner in the boundary map row and should be reconciled with the map before any ownership-changing optimization.

## Dependency Context

- F03A: CRM retrieval/update operations used by `FeeDownUpLoader` and fee commit paths.
- B01: session/login identity and authentication context used by `BaseChurchController`, `AuthenticationController`, and `FeeList.EnsureLoginScope`.
- B06A: reference/list data dependency for fee-related options and shared reference boundaries.
- X03: DevExtreme, layout, shared JavaScript, shared CSS, and route/static path behavior.

## Consumer Context

- B05 consumes the Fee master data contract for donation payment form choices and downstream payment flows.
- Static evidence shows donation-specific fee query code in `DonationFeeQueryService`, `DonationDedicationFeeFormService`, and `DonationPaymentManager`. Those are consumer/dependency context only, not B06B owner files.

## Gate And Tests

- Existing static route-path tests reference `/FeeManagement/LessonList/fake.js` in `ChurchReport.MemberInfo.Tests/StaticRequestPathHelperTests.cs`.
- The boundary map's B06A/B06B gate row requires List/Fee tests and B05 payment form/callback integration consumers before optimization.
- No restore/build/test was run during this diagnostic package creation because the requested CCG prompt must prohibit build/test/restore and the task is documentation-only.
