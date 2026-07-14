# B06B Extraction Analysis

## Ownership Shape

B06B is a cohesive business capability around lesson fee and present-fee master data, but the current implementation mixes four concerns:

- route/view orchestration in `FeeManagementController`
- session identity and scoped cache state in `FeeList` and `InMemoryDataContextSmallGroup`
- CRM retrieval/update behavior in `FeeDownUpLoader`
- UI grid behavior in Razor views and `FeeDataGridAjax.js`

## Extraction Risks

### E1 - Fee master data contract for B05 is not yet explicit

The boundary map marks B06B as owner of the Fee master data contract and B05 as the consumer. Existing B05 files use donation-specific fee list naming and query services. The diagnostic should keep donation transactions and provider callbacks out of B06B while documenting exactly which fee reference values B05 requires.

### E2 - `FeeList` is a mixed stateful domain/service/session object

`FeeList` owns login scope checks, lesson/present-fee loading, cached data lists, mutation application, pending change tracking, and batch commit. This makes it the central extraction blocker: a future B06B optimization should split immutable read models, session-scoped pending edits, and CRM commit operations.

### E3 - `FeeDownUpLoader` should become the CRM adapter seam

`FeeDownUpLoader` is the natural adapter around CRM lesson/fee/present-fee retrieval and update behavior, but it currently returns UI-facing models. A stable adapter should expose fee DTOs independent of Razor/ViewBag conventions.

### E4 - X03 shared UI assets must remain outside B06B

The boundary map states shared scripts such as `Ajax.js`, `DataGridAjax.js`, `DropDownBox.js`, `LoadPanel.js`, and `SelectDate.js` belong to X03. B06B may own `FeeDataGridAjax.js` only; extraction must avoid absorbing shared UI assets.

## Recommended Sequencing

1. Validate route/auth/session/cache behavior for current FeeManagement paths.
2. Document the B05-facing Fee master data contract with sample fields and ownership.
3. Extract a B06B read/write service interface behind `FeeList` behavior.
4. Move CRM-specific calls behind a `FeeDownUpLoader` adapter contract.
5. Replace dynamic ViewBag column mapping with typed view models after tests cover current behavior.
