# B04C Scope Manifest

Module: B04C scheduling QR
Workspace: docs/project-modular-diagnostics/B04C-scheduling-qr/
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Boundary Source

- Map: docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md
- B04C row: Scheduler API/UI and personal/group/Sunday QR generation and operations.
- Explicit inclusions: scheduler controller/API/UI, QR page controllers, course/personal/small-group/Sunday QR scan flows, and QR-driven CRM updates.
- Explicit exclusions: group master data, LINE transport internals, and attendance master data except as dependencies or consumers.
- Dependency context only: F03A CRM operations, F06 LINE notification workflow, B01 authentication/session, B02 member/contact data, B04A attendance/present-record consumers, B04B appointment equipment, and X03 shared UI assets.

## Primary Owner Files Reviewed

- SpeechMessageProducts.ChurchReport/Controllers/SchedulerController.cs
- SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs
- SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs
- SpeechMessageProducts.ChurchReport/Models/AppointmentsListManager.cs
- SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs
- SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
- SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
- SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
- SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
- SpeechMessageProducts.ChurchReport/Views/Home/Scheduler.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/SchedulerView.cshtml
- SpeechMessageProducts.ChurchReport/Views/QrCode/QrCodeView.cshtml
- SpeechMessageProducts.ChurchReport/Views/QrCode/PersonalQrCodeView.cshtml
- SpeechMessageProducts.ChurchReport/Views/QrCode/SmallGroupQrCodeView.cshtml
- SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml

## Dependency/Consumer Files Read For Context Only

- SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs
- SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
- SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs

## Boundary Decisions

- QR scan POST endpoints and their QR utility side effects are B04C because QrCodeController and QR utilities own the scan orchestration and choose which CRM records to mutate.
- SchedulerDataController and scheduler views are B04C because they expose the scheduler API/UI surface, even though some appointment persistence behavior overlaps B04B dependency context.
- Appointment CRM persistence details in AppointmentsDownUpLoader are cited only as dependency context for scheduler behavior; appointment/equipment ownership remains B04B.
- Present-record and attendance entities are cited only as B04A consumer/dependency data affected by QR scans; B04C owns the QR write path, not the attendance master-data contract.
- LINE SDK/LIFF transport is cited only at the integration boundary; F04/F06/B07 own reusable LINE transport internals.

