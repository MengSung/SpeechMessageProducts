# B04B Runtime Validation Plan

No runtime validation was executed in this diagnostic pass. The following plan is for a later implementation/validation task.

## B04B-SEC-001 Identity Pivot Validation

- Goal: prove whether `LoadAppointmentByLineId` can pivot an existing or fallback session to an arbitrary LINE user id.
- Setup:
  - Use a local/test environment with safe CRM fixtures or mocks.
  - Create two test contacts with distinct LINE ids and appointment visibility.
- Steps:
  - Authenticate as user A through the normal path.
  - POST to `/Appointment/LoadAppointmentByLineId` with user B's LINE id.
  - Inspect `_LoginAccount`, `_LoginPassword`, `_SessionUserId`, auth claims, and subsequent appointment/equipment data.
  - Repeat without an authenticated cookie but with only session fallback if the environment allows it.
- Expected safe result:
  - The endpoint rejects forged LINE ids unless a verified LINE/LIFF proof for the same subject is present.
  - The auth ticket subject and session identity cannot be changed by request body alone.

## B04B-PERF-001 CRM Query Count Validation

- Goal: measure current CRM call count and latency for equipment lesson/status grids.
- Setup:
  - Use existing `PerfPhase` labels: `Equipment.LoadEquipmentContact`, `Equipment.LoadEquipmentStorLessons`, and nested Retrieve phases.
  - Test datasets: 1 group x 5 contacts, 5 groups x 25 contacts, and 10 groups x 100 contacts with representative lesson rows.
- Steps:
  - Load `/Equipment/EquipmentView`.
  - Expand one group, then all visible contacts.
  - Capture CRM query count, elapsed time, allocations if available, and server log volume.
- Expected optimization target:
  - Replace per-contact/per-lesson entity retrieval with batched projection.
  - Reduce query count from O(groups x contacts x lessons) toward O(page/group query count).

## B04B-PERF-002 UI Expansion Validation

- Goal: quantify how `AutoExpandAll(true)` in `EquipmentContactView` amplifies backend requests.
- Steps:
  - Compare request count and latency with auto-expand enabled vs. disabled.
  - Verify row hover behavior can be implemented with CSS without per-row listener allocation.
- Expected safe result:
  - Detail lesson requests happen only when a user expands a contact, or under a bounded page-size strategy.
