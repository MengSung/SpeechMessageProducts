# B04C Extraction Analysis

## Summary

B04C has clean extraction candidates around QR scan command handling and scheduler query/command separation. These candidates preserve module boundary discipline: B04C owns scan/scheduler orchestration, while B01 owns identity/session policy, F03A owns CRM primitives, F06/B07 own reusable LINE workflows, B04A owns attendance master data, and B04B owns appointment/equipment persistence details.

## Candidates

### EXT-001 Verified QR scan command service

- Owning files:
  - SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs
  - SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
  - SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
  - SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
  - SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
  - SpeechMessageProducts.ChurchReport/Views/QrCode/*.cshtml
- Cohesive responsibility:
  - Convert a validated LINE/LIFF subject plus QR id into exactly one QR scan command result.
- Proposed contract:
  - Input: QR kind, QR id, scan type, validated LINE subject, display context, optional group/room context, idempotency key.
  - Output: scan status, display name fields, duplicate/created/updated state, and user-safe message.
  - Dependencies: B01 trusted identity proof contract, F03A CRM operations, F06/B07 notification workflow as needed, B04A present-record/attendance consumer contract.
  - Consumers: QrCodeController and QR views.
- Why this accelerates later work:
  - Removes repeated trust and parsing logic from four QR actions.
  - Makes forged LINE id, replay, duplicate scan, and missing-record tests explicit.
  - Creates one place to add batching/idempotency without changing LINE transport internals or attendance master data.
- Test seam:
  - Fake verified LINE subject and fake CRM present-record/query results.
  - Verify no CRM write happens when proof is missing or mismatched.

### EXT-002 Scheduler read/query and command boundary

- Owning files:
  - SpeechMessageProducts.ChurchReport/Controllers/SchedulerController.cs
  - SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs
  - SpeechMessageProducts.ChurchReport/Models/AppointmentsListManager.cs
  - SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs
  - SpeechMessageProducts.ChurchReport/Views/Home/Scheduler.cshtml
  - SpeechMessageProducts.ChurchReport/Views/Home/SchedulerView.cshtml
- Cohesive responsibility:
  - Serve scheduler visible-range rows and accept scheduler commands under an explicit authorization and ownership contract.
- Proposed contract:
  - Read input: authenticated/validated subject, visible date range, scheduler display type, resource filters, load options.
  - Read output: projected scheduler rows for DevExtreme.
  - Command input: appointment id/new row, operation type, concurrency token or expected owner.
  - Command output: accepted/rejected result and updated row id.
  - Dependencies: B04B appointment persistence, F03A CRM query/update primitives, B01 identity/session.
  - Consumers: B04C scheduler views and DevExtreme API surface.
- Why this accelerates later work:
  - Separates in-memory UI caching from CRM ownership and write behavior.
  - Allows query-window performance work without changing B04B appointment internals first.
- Test seam:
  - Fake scheduler repository returning rows for a date window.
  - Verify commands reject missing owner, stale appointment id, and unauthorized schedule type.

## Not Extraction Targets For B04C

- Group master-data CRUD and reporting: B03.
- Appointment/equipment domain persistence internals: B04B.
- Attendance/present-record master-data contract: B04A.
- LINE HTTP transport and reusable notification/reply workflows: F04/F06/B07.
- Global session, cookie, and antiforgery policy: B01/X01.
- Shared DevExtreme/static asset packaging: X03.

