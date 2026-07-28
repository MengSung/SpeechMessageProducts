# Package 1 ChurchReport consumer migration matrix

Date: 2026-07-25

## Feature flag

`DynamicsAccess:Package01FeeReadsEnabled` (default `false`)

When false, all paths below keep legacy ToolUtility behavior.

## Safe enable tiers (non-prod first)

See also: `phase3-enablement-rollback.md`

| Tier | Consumers | Enable only after |
| --- | --- | --- |
| **A** | DonationFeeQueryService fee date-range | unit/smoke green; Gateway or Embedded preflight OK |
| **B** | Equipment / MemberInfo / DownloadEquipment / EquipmentStatusCalculator stor-by-contact | Tier A parity on non-prod |
| **C** | FeeDownUpLoader present/enroll/process by discipleLesson | Tier B parity |
| **D** | PollManager / QrCodeUtility contact+lesson find | Tier C parity; highest user-facing blast radius among Package 1 reads |

Production: only after A-D green on non-prod, with operator ready to set flag `false`.

## Migrated read paths (Package 1 capable)

| Consumer | Legacy entry | Package 1 capability | Status | Enable tier |
| --- | --- | --- | --- | --- |
| DonationFeeQueryService | RetrieveDedicationFeeByDateFetchXml | fee.dedication.retrieve.by.contact.date.range | Wired | A |
| EquipmentController.LoadEquipmentStorLessons | RetrieveStorLessonsByFetchXml(contact) | lessons.stor.retrieve.by.contact | Wired | B |
| MemberInfoController.LoadContactStorLessons | RetrieveStorLessonsByFetchXml(contact) | lessons.stor.retrieve.by.contact | Wired | B |
| FeeDownUpLoader.ProcessDiscipleLesson | RetrieveStorLessonsByDiscipleLessonsFetchXml | lessons.stor.retrieve.by.disciplelesson | Wired | C |
| FeeDownUpLoader.ProcesseLessonsList enroll count | QueryEntityList(disciple->stor) | lessons.stor.retrieve.by.disciplelesson | Wired | C |
| FeeDownUpLoader.ProcesseDiscipleLessons | QueryEntityList(disciple->stor) | lessons.stor.retrieve.by.disciplelesson | Wired | C |
| PollManager.RetrieveStorLesson | RetrieveStorLessonsByFetchXml(lesson+contact) | contact list + filter discipleLesson | Wired | D |
| QrCodeUtility.SigningLesson | RetrieveStorLessonsByFetchXml(lesson+contact) | contact list + filter discipleLesson | Wired | D |
| DownloadEquipment stor list | RetrieveManyToOneRelationship(contact->stor) | lessons.stor.retrieve.by.contact | Wired | B |
| EquipmentStatusCalculator | RetrieveManyToOneRelationship(contact->stor) | lessons.stor.retrieve.by.contact | Wired | B |

## Not migrated yet (still legacy / write / out of Package 1)

| Consumer | Why deferred |
| --- | --- |
| FeeDownUpLoader UpdateFeeDataList / CreateFee | write path, not Package 1 read |
| PollManager/QrCode CreateNewStorLesson | write path |
| DonationFeePaymentProcessor stor update | write path after payment |
| QrCodeUtility option-set mapping | metadata; Package 0 has option-set op, not wired here yet |
| SmallGroupQrCodeUtility | commented/legacy small-group path |
| Arbitrary QueryEntityListByDate for disciple lessons lists | different entity (new_disciple_lessons), not stor/fee Package 1 templates |

## Architecture notes

- Products never reference WebApi; only ProductClient + Embedded + Abstractions.
- StorLessonQueryService is the single ChurchReport switchboard for stor-lesson list reads.
- Package01 list queries may still RetrieveEntity for detail fields required by existing processors.
- PowerPlatform.Dataverse.Client remains until Phase 6 after all consumers migrate.
- Stabilization gate docs: `phase3-enablement-rollback.md`, `phase3-stabilization-verification.md`.

## Operator runbooks

- Tier A deep checklist: `phase3-tier-a-enablement-checklist.md`
- Live smoke attempt log: `phase3-live-smoke-attempt.md`
