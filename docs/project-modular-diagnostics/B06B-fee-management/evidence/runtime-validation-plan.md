# B06B Runtime Validation Plan

## Validation Goals

- Prove active B06B routes require the expected authenticated session.
- Prove fee/present-fee data and pending changes are isolated by login/session.
- Prove mutating fee APIs reject missing/invalid anti-forgery or unauthorized requests, or record that protection is missing.
- Measure list load and save latency for representative fee datasets.
- Confirm B05 consumes only stable B06B fee master data outputs, not B06B implementation internals.

## Proposed Checks

1. Route/auth smoke:
   - Request `/FeeManagement/LessonList`, `/FeeManagement/Fee/{id}`, `/FeeManagement/Present/{id}`, `/FeeManagement/Api/Lessons`, and `/FeeManagement/Api/FeeData` without a valid login.
   - Expected result: redirect or rejection consistent with B01 session policy.
2. CSRF/write API validation:
   - Submit `PUT /FeeManagement/Api/UpdateFeeData` and `POST /FeeManagement/Api/SaveBatch` without anti-forgery proof.
   - Expected result: rejection, or a documented security finding if accepted.
3. Session isolation:
   - Login as user A, stage a fee edit, switch/login as user B in the same browser or a parallel session, then call `SaveBatch`.
   - Expected result: user A pending edits are cleared or inaccessible to user B.
4. Data-size performance:
   - Capture timings for `SetupLessonList`, `SetupPresentFeeList`, `DataSourceLoader.Load`, and `CommitPendingChanges` using existing `PerfPhase` markers.
   - Expected result: identify whether full-list materialization is an observed bottleneck.
5. Consumer contract:
   - Exercise the B05 donation payment form path that reads fee choices.
   - Expected result: B05 depends on a stable fee master data contract only, with provider callbacks still owned by B05/F08/F09.

## Out Of Scope

- `dotnet restore`, `dotnet build`, `dotnet test`, package restore, code generation, formatting, migrations, and product code changes are not part of this diagnostic run.
- Donation payment transaction execution and provider callback protocol validation are excluded except as B05 consumer context.

## Bounded Validation Outcome - 2026-07-13

| ID | Measurement | Result | Disposition |
|---|---|---|---|
| B06B-RV-001 | Static anti-forgery coverage for `UpdateFeeData` and `SaveBatch` | No local attribute and no global auto-validation filter | `STATIC_CONFIRMED_MISSING_ANTIFORGERY` |
| B06B-RV-002 | `FeeList.EnsureLoginScope` clears user-scoped lists and pending changes | Guard exists; no targeted executable test | `RUNTIME_VALIDATION_PENDING_NO_TEST_SEAM` |
| B06B-RV-003 | 10/100/1,000-record setup/load/commit timing | Concrete CRM loader and no fake/isolated tenant | `BLOCKED_NO_FAKE_OR_ISOLATED_CRM` |
| B06B-RV-004 | B05 consumes only a stable fee contract | Contract extraction not implemented | `BLOCKED_CONTRACT_NOT_IMPLEMENTED` |

Running controller or CRM scenarios against production is prohibited. No safe
existing runtime command can close B06B-RV-002 through RV-004 without product
test-seam work, so the module remains `RUNTIME_VALIDATION_PENDING`.
