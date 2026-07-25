# Package 1 ChurchReport consumer migration matrix

Date: 2026-07-25

## Feature flag

`DynamicsAccess:Package01FeeReadsEnabled` (default `false`)

When false, all paths below keep legacy ToolUtility behavior.

## Migrated read paths (Package 1 capable)

| Consumer | Legacy entry | Package 1 capability | Status |
| --- | --- | --- | --- |
| DonationFeeQueryService | RetrieveDedicationFeeByDateFetchXml | fee.dedication.retrieve.by.contact.date.range | Wired |
| EquipmentController.LoadEquipmentStorLessons | RetrieveStorLessonsByFetchXml(contact) | lessons.stor.retrieve.by.contact | Wired |
| MemberInfoController.LoadContactStorLessons | RetrieveStorLessonsByFetchXml(contact) | lessons.stor.retrieve.by.contact | Wired |
| FeeDownUpLoader.ProcessDiscipleLesson | RetrieveStorLessonsByDiscipleLessonsFetchXml | lessons.stor.retrieve.by.disciplelesson | Wired |
| FeeDownUpLoader.ProcesseLessonsList enroll count | QueryEntityList(disciple->stor) | lessons.stor.retrieve.by.disciplelesson | Wired |
| FeeDownUpLoader.ProcesseDiscipleLessons | QueryEntityList(disciple->stor) | lessons.stor.retrieve.by.disciplelesson | Wired |
| PollManager.RetrieveStorLesson | RetrieveStorLessonsByFetchXml(lesson+contact) | contact list + filter discipleLesson | Wired |
| QrCodeUtility.SigningLesson | RetrieveStorLessonsByFetchXml(lesson+contact) | contact list + filter discipleLesson | Wired |
| DownloadEquipment stor list | RetrieveManyToOneRelationship(contact->stor) | lessons.stor.retrieve.by.contact | Wired |
| EquipmentStatusCalculator | RetrieveManyToOneRelationship(contact->stor) | lessons.stor.retrieve.by.contact | Wired |

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