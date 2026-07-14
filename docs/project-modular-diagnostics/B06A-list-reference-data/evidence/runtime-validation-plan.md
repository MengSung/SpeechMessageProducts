# B06A Runtime Validation Plan

## Status

B06A is gate-blocked in the module map because B06A-B06C have no directly attributable existing test suite. Runtime validation is therefore required before any optimization claim.

## Validation Objectives

1. Prove all ListManagement and option metadata routes require the expected authentication and authorization context.
2. Prove reference/list cache keys are isolated by all relevant church/user/role context dimensions.
3. Measure duplicate CRM/list/metadata calls during representative ListManagement page loads.
4. Confirm B05, B06B, and B06C consume B06A through an explicit reference/list contract rather than private implementation details.
5. Confirm `MapData` and `MapDataList` callers match B06A ownership and do not encode fee/register/payment workflow policy.

## Proposed Gate Commands

These are proposed only and were not executed in this diagnostic:

- Static caller inventory for `ListManagementController`, `OptionSetMetadataService`, `OptionSetConverter`, `MapData`, and `MapDataList`.
- Route/auth smoke tests for ListManagement and metadata endpoints.
- Cache isolation tests with two distinct user/church contexts.
- Request-level instrumentation for CRM/list/metadata call counts.
- Consumer compile/integration gate for B05, B06B, and B06C after B06A contract extraction.

## Prohibited During CCG Review

The CCG review prompt prohibits restore/build/test, package restore, code generation, formatting, migrations, and writes outside the allowed B06A diagnostic and b06a-prefixed CCG paths.

## Bounded Validation Outcome - 2026-07-13

| ID | Measurement | Result | Disposition |
|---|---|---|---|
| B06A-RV-001 | Search for concrete `IListManagementService` implementation and DI registration | No implementation; no host registration; B02 `ContactService` consumes the interface | `STATIC_CONFIRMED_UNREGISTERED_AND_CURRENTLY_UNREACHABLE` |
| B06A-RV-002 | Route/auth proof | No targeted executable test | `BLOCKED_NO_ROUTE_TEST_SEAM` |
| B06A-RV-003 | Mutable ListManagement cache isolation across user/church contexts | No targeted executable test; exclude schema-only `OptionSetMetadataService` cache from user-isolation claim | `BLOCKED_NO_CACHE_TEST_SEAM` |
| B06A-RV-004 | CRM/list/metadata call count | No injectable CRM counter or isolated fixture | `BLOCKED_NO_INSTRUMENTATION_OR_ISOLATED_CRM` |
| B06A-RV-005 | B05/B06B/B06C provider-consumer contract | Contract not yet extracted; product change is out of scope | `BLOCKED_CONTRACT_NOT_IMPLEMENTED` |

No runtime command can safely produce the missing evidence without adding test
seams or using an isolated CRM fixture. The module therefore remains
`RUNTIME_VALIDATION_PENDING`.
