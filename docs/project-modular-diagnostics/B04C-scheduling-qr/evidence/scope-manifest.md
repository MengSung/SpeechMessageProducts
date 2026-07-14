# B04C Scope Manifest

## Module Identity

- Leaf ID: B04C
- Workspace: docs/project-modular-diagnostics/B04C-scheduling-qr/
- Mode: DIAGNOSIS_ONLY
- Gate status: BLOCKED
- Module map row: B04C owns Scheduler API/UI and personal/small-group/Sunday QR generation and operations.

## Primary Owner Files

- SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs
- SpeechMessageProducts.ChurchReport/Controllers/SchedulerController.cs
- SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs
- SpeechMessageProducts.ChurchReport/Services/SundayCalculator.cs
- SpeechMessageProducts.ChurchReport/Services/WeeklyScheduleSettings.cs
- SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
- SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
- SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
- SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
- SpeechMessageProducts.ChurchReport/Models/HolidayClass.cs
- SpeechMessageProducts.ChurchReport/Models/PollManager.cs
- SpeechMessageProducts.ChurchReport/Models/PollModel.cs
- SpeechMessageProducts.ChurchReport/Views/QrCode/**
- SpeechMessageProducts.ChurchReport/Views/Home/Scheduler.cshtml
- SpeechMessageProducts.ChurchReport/Views/Home/SchedulerView.cshtml
- SpeechMessageProducts.ChurchReport/wwwroot/css/Scheduler.css

## Explicit Exclusions

- LINE transport belongs outside B04C.
- Small-group master data belongs outside B04C.
- Attendance data master ownership belongs outside B04C.
- Appointment/equipment workflow ownership belongs to B04B; B04C owns only the scheduler API/UI surfaces listed in the map.

## Dependencies

- F03A CRM operations library and ToolUtility APIs.
- F06/B07 LINE workflow consumers for QR user flows.
- B01 identity/session/access control.
- B04A attendance/present-record data concepts.
- B04B appointment/equipment data concepts.
- X03 UI assets and DevExtreme scheduler assets.

## Consumers

- QR scan routes mapped in Startup.cs for course, poll, small-group, Sunday, and personal QR views.
- DevExtreme scheduler view and SchedulerDataController endpoints.
- LINE LIFF browser flows that post identity/profile data back to B04C endpoints.

## Tests And Gate Notes

- Module map marks B03/B04A-B04C/B06A-B06C as known gate-blocked because no directly owned existing test suite exists.
- Consumer gates require B04C scheduler/QR integration with B04A attendance, B04B appointment/equipment boundaries, B01 identity, and F06/B07 LINE workflows.

## Write Scope Verification

- This diagnostic writes only under docs/project-modular-diagnostics/B04C-scheduling-qr/** and b04c-prefixed .ccg/dual-model-runs artifacts.
- Product code, config, tests, generated outputs, bin, obj, cache, and lockfiles are not modified.
- Nested agent count: 0
