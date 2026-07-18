# B04C Performance Analysis

## Summary

B04C has two clear performance/design issues. Sunday/personal QR scans can perform deeply nested CRM reads/writes during a single scan, and the scheduler API materializes and filters cached appointment collections rather than using bounded server-side query windows. These are source-confirmed design issues; no runtime measurement was executed in this diagnostic pass.

## Findings

### B04C-PERF-001 Sunday/personal QR scan can fan out into nested CRM read/write loops

- Evidence:
  - Sunday QR setup resolves the meeting statistic, contact, present records, and then can create present records when missing at SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:147-223 and 254-321.
  - `SetPresentRecordTimeAttribute` updates present-record fields and may send LINE notification at SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:388-427.
  - When a Sunday/personal present record must be created, `CreatePresentRecordOnSmallGroup` loops all lists for the contact, retrieves list, leader, generated weekly reports, weekly report entities, present records, and updates weekly-report saved flags at SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:841-904.
  - Sunday QR UI tells users to wait 5-10 seconds after scan at SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml:113-126, consistent with a heavy synchronous scan path.
- Design problem:
  - A single QR scan can synchronously execute CRM list discovery, weekly-report generation, present-record lookup/create, updates, and optional messaging.
  - The path mixes read-model discovery, write command handling, record creation, attendance calculation triggers, and UI response assembly.
- Impact:
  - QR scan latency and CRM load scale with the number of groups/lists and generated weekly reports for a user.
  - Long synchronous scan handling increases retry/replay risk and can duplicate work if users resubmit during the wait window.
- Recommended action:
  - Introduce a B04C QR scan command service with an idempotency key and a bounded present-record resolver.
  - Split expensive weekly-report/present-record creation into an explicit queued or guarded path where feasible.
  - Measure per-scan CRM call count and latency before and after extraction.

### B04C-PERF-002 Scheduler API uses session-cached collection materialization instead of bounded query contract

- Evidence:
  - SchedulerDataController loads `DataSourceLoader.Load(_data.Appointments, loadOptions)` against an in-memory collection at SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:41-45.
  - The collection is cached by session id and initialized from `AppointmentsListManager.m_Appointments` at SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs:34-53.
  - Scheduler views configure DevExtreme scheduler WebApi load/insert/update/delete endpoints and multiple month/agenda/timeline views at SpeechMessageProducts.ChurchReport/Views/Home/Scheduler.cshtml:15-22, 31-68, and 327-380.
  - Appointment dependency code can retrieve a monthly appointment range plus lesson-derived appointments at SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:144-163 and 172-180.
- Design problem:
  - The scheduler surface lacks a clear date-window/page/query contract owned by B04C.
  - Session-cached collections can become stale and force load/filter behavior into memory rather than a bounded CRM projection.
- Impact:
  - Scheduler latency and memory can grow with appointment volume per session.
  - Mutation behavior is harder to reason about because memory cache state and CRM persistence are separated across B04C/B04B boundary context.
- Recommended action:
  - Extract a scheduler read/query boundary that accepts visible date range, schedule type, current subject, and resource filters.
  - Return projected scheduler rows and defer CRM persistence to explicit command methods with ownership checks.

## Runtime Evidence Status

- No restore, build, test, codegen, formatting, migration, or runtime profiling was run due diagnostic-only constraints.
- Runtime validation is needed for CRM call counts, duplicate scan behavior, and scheduler request latency.

