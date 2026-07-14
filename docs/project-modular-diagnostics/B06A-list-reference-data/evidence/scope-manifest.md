# B06A Scope Manifest - List Reference Data

## Diagnostic Workspace

- Workspace: `B06A-list-reference-data`
- Leaf ID: `B06A`
- Nested agent count: `0`
- Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
- Source workflow: `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`
- Source boundary map: `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`

## Boundary Row Summary

B06A owns the ChurchReport list and reference-data surface:

- `ListManagement`
- `option metadata`
- `map/list reference data`

Explicit exclusions are:

- fee maintenance, owned by B06B except where it consumes B06A reference/list contracts
- donation transaction and payment flows, owned by B05/F08/F09 except where they consume B06A reference/list contracts
- church register and hierarchy flows, owned by B06C except where they consume B06A reference/list contracts

Primary dependencies listed by the boundary map are F03A, B01, X02A, and X03.

## Primary Owner Candidates

The boundary map names these B06A-owned paths directly:

- `SpeechMessageProducts.ChurchReport/Controllers/ListManagementController.cs`
- `SpeechMessageProducts.ChurchReport/Services/ListManagement/**`
- `SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs`
- `SpeechMessageProducts.ChurchReport/Utilities/OptionSetConverter.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Models/MapData.cs`
- `SpeechMessageProducts.ChurchReport/Models/MapDataList.cs`

Additional in-scope reference/list candidates observed by filename inventory:

- `SpeechMessageProducts.ChurchReport/Models/ListManager.cs`
- `SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs`
- `SpeechMessageProducts.ChurchReport/Extensions/ListManagerCacheExtensions.cs`
- `SpeechMessageProducts.ChurchReport/Views/Home/ListManagement.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/ListManagementDistrictPastor.cshtml`

## Dependency / Consumer Context

- B06A provides the `Reference/list contract` consumed by B05, B06B, and B06C.
- CCG review found an additional code-level B02 consumer candidate:
  `SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs`
  injects `IListManagementService`, while the boundary map's reference/list contract
  consumer row lists only B05, B06B, and B06C. This is a boundary-map/documentation
  gap to resolve before extraction.
- B06B consumes list/reference data for fee master-data maintenance.
- B05 consumes list/reference data for donation/payment form and callback integration context.
- B06C consumes list/reference data for church hierarchy/register context.
- F03A and B01 are upstream platform/security dependencies for ChurchReport module behavior.
- X02A and X03 provide shared cache and browser/shared asset support.

## Gate State

The module map marks B06A-B06C as gate-blocked because no directly attributable existing test suite is available. This diagnostic therefore stops at analysis/diagnosis and proposes runtime validation instead of declaring optimization complete.
