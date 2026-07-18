# B04C Runtime Validation Plan

No runtime validation was executed in this diagnostic pass. The following plan is for a later safe test environment.

## B04C-SEC-001 Forged LINE Subject Validation

- Goal: prove whether B04C QR POST endpoints can mutate scan/attendance state using a forged `UserLineId`.
- Setup:
  - Use safe CRM fixtures or mocks with two contacts: user A and user B.
  - Create one course QR, one small-group QR, one Sunday QR, and one personal QR fixture.
- Steps:
  - Open each QR page normally as user A and capture the legitimate request shape.
  - Replay the POST to `QrCodeGetLineId`, `SmallGroupQrCodeGetLineId`, `SundayQrCodeGetLineId`, and `PersonalQrCodeGetLineId` with user B's LINE id and no server-verifiable LINE token.
  - Repeat with mismatched token/user once token verification exists.
  - Inspect CRM present-record/course/weekly-report/contact/member-list side effects.
- Expected safe result:
  - Every QR POST rejects missing, forged, or mismatched LINE proof.
  - No CRM write occurs until server-side token validation proves the posted subject.

## B04C-PERF-001 QR Scan CRM Call Count Validation

- Goal: measure current CRM call count and elapsed time for Sunday/personal QR scans.
- Dataset:
  - Contact with no small group.
  - Contact with 1 group and 1 weekly report.
  - Contact with multiple groups and generated weekly reports.
- Steps:
  - Capture elapsed time and CRM read/write counts for one scan per QR kind.
  - Repeat duplicate scan and browser retry scenarios.
  - Record whether saved-flag recalculation is triggered once or multiple times.
- Expected optimization target:
  - QR command service resolves the relevant present record with bounded calls and idempotent write behavior.

## B04C-PERF-002 Scheduler Query Validation

- Goal: quantify scheduler load behavior for month, agenda, and timeline views.
- Dataset:
  - Small month: under 50 appointment rows.
  - Large month: hundreds or thousands of appointment/lesson-derived rows.
- Steps:
  - Load scheduler month, agenda, and timeline views.
  - Change current date and current view repeatedly.
  - Capture request count, elapsed time, memory/cache size if available, and rows materialized.
- Expected optimization target:
  - Scheduler read path accepts a visible date window and returns projected rows without session-wide collection materialization.

