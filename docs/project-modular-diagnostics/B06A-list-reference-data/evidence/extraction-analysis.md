# B06A Extraction and Acceleration Analysis

## Extraction Goal

Define a stable B06A reference/list contract that can be consumed by B05, B06B, and B06C without those modules owning list retrieval, option metadata conversion, or map/list model internals.

## Findings

### E1 - B06A should expose a narrow reference/list contract before B06B/B06C optimization

- Rank: High
- Type: Extraction / dependency control
- Evidence: The boundary map lists `Reference/list contract | B06A | B05, B06B, B06C` and dependency direction `B06A => B06B/B06C/B05`. CCG review also found `Services/Contact/Impl/ContactService.cs` injects `IListManagementService`, making B02 an unlisted code-level consumer candidate.
- Risk: If B05/B06B/B06C or B02 call B06A internals directly, optimization in any consumer can force cross-module edits and blur ownership.
- Current diagnostic conclusion: Confirmed design risk from the module map plus a CCG-confirmed static caller gap; implementation details still require caller inventory.
- Required validation: Produce a caller map for B02/B05/B06B/B06C references to ListManagement, list models, option metadata, and map/list classes.

### E2 - Option metadata conversion is a likely extraction seam

- Rank: Medium
- Type: Extraction / acceleration
- Evidence: `OptionSetMetadataService.cs` and `OptionSetConverter.cs` are named B06A owners.
- Risk: Metadata conversion can be isolated behind an interface to reduce CRM SDK coupling in UI and consumer modules.
- Current diagnostic conclusion: Candidate acceleration. Need static caller proof before modifying code.
- Required validation: Inventory callers and define expected DTO/value object shape.

### E3 - MapData and MapDataList need explicit ownership comments or contract tests

- Rank: Medium
- Type: Extraction / ownership proof
- Evidence: The module map explicitly notes that `MapData.cs` and `MapDataList.cs` are corrected to B06A unique ownership.
- Risk: These files were important enough to call out as corrected ownership, so future agents may misclassify them without local evidence.
- Current diagnostic conclusion: Confirmed ownership documentation need; no code change in this diagnostic.
- Required validation: Add B06A gate tests or manifest references when test baseline is established.

### E4 - Services/ListManagement has an unresolved implementation and consumer gap

- Rank: High
- Type: Extraction / ownership proof
- Evidence: CCG review found `IListManagementService.cs` under `Services/ListManagement/**` but no concrete implementation by static search, while `ContactService.cs` injects `IListManagementService`.
- Risk: The interface may be dead code, an unfinished service seam, or a DI failure path if `ContactService` is resolved without a registered implementation.
- Current diagnostic conclusion: CCG-confirmed static issue; runtime behavior remains unvalidated.
- Required validation: Verify DI registrations and whether `ContactService` is reachable. If dead code, mark for removal or ownership transfer; if live, add implementation/registration under the correct module plan.

## Acceleration Opportunities

- Create an `IReferenceListService` or equivalent B06A-owned contract only after caller inventory proves a stable shape, including the B02 `ContactService` consumer candidate.
- Keep fee-specific fields in B06B and register/hierarchy-specific behavior in B06C; B06A should provide reference values, not consumer workflow policy.
- Treat B06A as a provider module for B05/B06B/B06C, not a shared dumping ground for all master-data code.
