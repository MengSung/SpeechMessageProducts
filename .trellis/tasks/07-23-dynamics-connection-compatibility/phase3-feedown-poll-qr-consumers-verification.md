# Phase 3 continuation: FeeManagement / Poll / QR stor-lesson consumers

Date: 2026-07-25

## Done
- Expanded StorLessonQueryService:
  - GetByContact
  - GetByDiscipleLesson
  - GetEntityCollectionByDiscipleLesson
  - FindStorLessonId (contact + discipleLesson)
- Wired consumers (behind DynamicsAccess:Package01FeeReadsEnabled):
  - FeeDownUpLoader.ProcessDiscipleLesson (FeeManagement present fee list)
  - PollManager.RetrieveStorLesson
  - QrCodeUtility.SigningLesson
  - Existing: EquipmentController, MemberInfoController, Donation fee date-range

## Verification
- ChurchReport build: success
- Dynamics.Tests / SmokeTests: run after this note

## Default
Package01FeeReadsEnabled remains false (legacy path).