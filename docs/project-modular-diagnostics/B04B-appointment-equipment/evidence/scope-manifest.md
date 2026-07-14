# B04B Scope Manifest

Module: B04B appointment equipment
Workspace: docs/project-modular-diagnostics/B04B-appointment-equipment/
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Boundary Source

- Map: docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md
- B04B row: appointment, equipment borrowing, lesson/course, equipment status, and related UI.
- Explicit exclusions: attendance, scheduling/QR, and unrelated business modules.
- Dependency context only: F03A CRM operations, B01 authentication/session, B02 member/contact profile, X03 shared UI.
- Consumer context only: B04A attendance and B04C scheduling/QR, without diagnosing those modules.

## Primary Owner Files Reviewed

- SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs
- SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs
- SpeechMessageProducts.ChurchReport/WebServiceConnector/EquipmentStatusCalculator.cs
- SpeechMessageProducts.ChurchReport/Models/Appointment.cs
- SpeechMessageProducts.ChurchReport/Models/AppointmentsListManager.cs
- SpeechMessageProducts.ChurchReport/Models/EquipmenSmallGroup.cs
- SpeechMessageProducts.ChurchReport/Models/EquipmentContact.cs
- SpeechMessageProducts.ChurchReport/Models/EquipmentDataManager.cs
- SpeechMessageProducts.ChurchReport/Models/EquipmentRootClass.cs
- SpeechMessageProducts.ChurchReport/Models/EquipmentStorLessons.cs
- SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs
- SpeechMessageProducts.ChurchReport/Models/Lesson.cs
- SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentView.cshtml
- SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentContactView.cshtml
- SpeechMessageProducts.ChurchReport/Views/Equipment/EquipmentStorLessonsView.cshtml

## Dependency/Consumer Files Read For Context Only

- SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs
- SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs
- SpeechMessageProducts.ChurchReport/Startup.cs
- ChurchReport.MemberInfo.Tests/StaticRequestPathHelperTests.cs

## Boundary Decisions

- Appointment and equipment CRUD/data-load findings are in B04B because the owning controllers and CRM connector files are in the B04B primary owner list.
- Authorization and session helper files are cited only to validate B04B security flow. They are not diagnosed as B01/X01 issues.
- DevExtreme shared assets, static request path helper behavior, and route registration are context only and not claimed as B04B ownership.
- Attendance/present-record and scheduling/QR code paths are excluded.
