# B04B Extraction Analysis

## Summary

B04B has a clean extraction candidate around equipment lesson/status read models and a smaller candidate around appointment LINE binding. Both can be isolated without owning attendance, scheduling/QR, or shared UI.

## Candidates

### EXT-001 Equipment lesson/status query service

- Owning files:
  - SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/EquipmentStatusCalculator.cs
  - SpeechMessageProducts.ChurchReport/Models/EquipmentContact.cs
  - SpeechMessageProducts.ChurchReport/Models/EquipmentStorLessons.cs
  - SpeechMessageProducts.ChurchReport/Models/EquipmenSmallGroup.cs
- Cohesive responsibility:
  - Load equipment group/contact/course status read models for B04B views.
- Proposed contract:
  - Input: authenticated contact id/session context, small group/list id, optional contact id, paging/filter options.
  - Output: equipment group summaries, contact equipment status rows, lesson rows with stage/date/complete fields.
  - Dependencies: F03A CRM query API only.
  - Consumers: B04B Equipment controller and Equipment views.
- Why this accelerates later work:
  - Enables batching, query projection, caching, and runtime profiling behind a single B04B-owned service.
  - Avoids changing B04A attendance or B04C scheduling/QR contracts.
- Test seam:
  - Fake CRM query result sets for contacts, stor lessons, and disciple lessons.
  - Verify no per-row CRM calls are required for common grid paths.

### EXT-002 Appointment LINE binding verifier

- Owning files:
  - SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs
  - SpeechMessageProducts.ChurchReport/Models/AppointmentsListManager.cs
  - SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs
- Cohesive responsibility:
  - Convert a trusted LINE/LIFF proof into B04B appointment session context.
- Proposed contract:
  - Input: signed/validated LINE identity proof and requested B04B view context.
  - Output: server-side appointment session identity and authorization result.
  - Dependencies: B01 authentication/session contract and F03A contact lookup.
  - Consumers: B04B appointment controller only.
- Why this accelerates later work:
  - Separates identity proof from appointment data operations.
  - Makes forged-user and cross-user session tests explicit.

## Not Extraction Targets For B04B

- Attendance/present-record services: B04A.
- Scheduler/QR controllers, Sunday QR helpers, and scheduler APIs: B04C.
- DevExtreme/static shared assets: X03.
- Generic session middleware, global auth filter, and route composition: B01/X01 context only.
