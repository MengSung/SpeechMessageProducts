# F06 Scope Manifest

Status: COMPLETE
Mode: STATIC_READ_ONLY
Leaf: F06 LINE Notification and Reply Workflows
Gate status: BLOCKED_PENDING_GREEN_PROVIDER_AND_CONSUMER_BASELINE

## Authoritative Boundary

The module map assigns F06:

- `LineMessagingProcessor.Workflows/**`;
- `LineMessagingProcessor.Workflows.Tests/**`;
- message factories;
- recipient validation;
- notification/reply result normalization.

Map evidence:

- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:104`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:142`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:704`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:742`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:830`

Primary dependencies are F04 LINE SDK contracts and F05A processor contracts.
Direct consumers include F05B, B04C, B05, and B07. Dependencies and consumers
were opened only to prove contract use, reachability, and ownership.

## Explicit Exclusions

The following are not F06-owned findings:

- ChurchReport CRM/profile lookup, contact binding, and product decisions;
- RichMenu catalog, provisioning, assignment, trigger, and expiry;
- F05A processor-core credential, disposal, profile, event, and RichMenu logic;
- F04 HTTP transport, serialization internals, provider status/header parsing,
  and SDK model implementation;
- F05B ASP.NET Core registration and host composition.

## Owned Production Inventory

| Path | Lines | SHA-256 | Responsibility |
| --- | ---: | --- | --- |
| `LineMessagingProcessor.Workflows/ILineNotificationWorkflow.cs` | 24 | `B51AE249B205008CCCEBE6F1CF4A3C92A29D807AE4C6E544603749ECE7DEFFDF` | notification contract |
| `LineMessagingProcessor.Workflows/ILineReplyWorkflow.cs` | 27 | `A0268BDB1E6783205194236A26E3B1FBDAF7589D4B4687916C8F9E6817C3EB82` | reply contract |
| `LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs` | 163 | `45DA68442788F4FDF262E1625F00BA7BE9FCB54A3955E967148FF3B71A92588B` | notification validation and normalization |
| `LineMessagingProcessor.Workflows/LineReplyWorkflow.cs` | 128 | `8A5BFA457700B76DCE1A1BB911399F3AA3A56E16B239B7C8674B16CD754B81F1` | reply validation and normalization |
| `LineMessagingProcessor.Workflows/LineNotificationRequest.cs` | 29 | `A334BDD24C4C0E2274C8F508D962C2AF7621A6815400F5A852B6E578148360B6` | notification input |
| `LineMessagingProcessor.Workflows/LineReplyRequest.cs` | 30 | `EB762DF0C46F43EEEDBDA39F5D0FBFFB3B787E117C8FED6B599D01BC83927290` | reply input |
| `LineMessagingProcessor.Workflows/LineNotificationResult.cs` | 75 | `03581351C66CD651D99C292A64756B2C21147F5FABAB884B9BF33833DB825844` | notification result |
| `LineMessagingProcessor.Workflows/LineReplyResult.cs` | 59 | `E0A0A15E7EF9001393C4DE943FF7C5E546793E94AC2625B090C9C4C588D38E23` | reply result |
| `LineMessagingProcessor.Workflows/LineNotificationException.cs` | 28 | `BBF2CD9E099F7AED8193F4727AA181A89B026EB36F7B65BDE422878F902D2A66` | throwing notification adapter |
| `LineMessagingProcessor.Workflows/LineReplyException.cs` | 30 | `085F4FE237B464C5B8BF1A89A45B81B429F5F2E5665C463B1B617BBDB354F2F8` | throwing reply adapter |
| `LineMessagingProcessor.Workflows/LineNotificationRecipient.cs` | 44 | `4B764A41708CE95EFEB128FA6DC53DE1FE5E6878726FD77C20A5E75E583EB66A` | recipient value object |
| `LineMessagingProcessor.Workflows/LineNotificationRecipientKind.cs` | 25 | `D735CCB29C74B45ED01665967AFDE1CBA575A505B20AD6CA4C2E7A6A46013DF3` | recipient discriminator |
| `LineMessagingProcessor.Workflows/LineNotificationContent.cs` | 241 | `943676C672B206647ED5601E5F555658F6EFA6BD69BE9B8F3E412B357FFD8012` | message content factories |
| `LineMessagingProcessor.Workflows/LineMessageFactoryValidation.cs` | 143 | `45B601710A2A5FCD7C48892D24193A4347CA6F95E11AE9E57AA09029C93CF693` | shared factory guards |
| `LineMessagingProcessor.Workflows/LineCarouselColumnFactory.cs` | 52 | `EAB17FE3599DDC882FE393B0F206D8D69AEB94E850A744BE8630E131ADBC140C` | carousel factories |
| `LineMessagingProcessor.Workflows/LineImagemapActionFactory.cs` | 34 | `152564AFE1913E84CB60A10146DD574D3B48C590E5F67B4EA35A8461F0C56E6B` | imagemap factories |
| `LineMessagingProcessor.Workflows/LineQuickReplyFactory.cs` | 63 | `546317EC836A09A172CB91610B30FFB26BB66165F46AB575EE02396D796FB1B2` | quick-reply factories |
| `LineMessagingProcessor.Workflows/LineTemplateActionFactory.cs` | 39 | `21C3B7AEF52111B849CAE8F4E32691754CD9917669E7F4B75F0E6A643EBF2BBB` | template action factories |
| `LineMessagingProcessor.Workflows/LineNotificationStatus.cs` | 26 | `58FB812E95C3E72D8CC237858F852C90FB883BC6BC19D01A734115A36F5F229E` | shared status enum |
| `LineMessagingProcessor.Workflows/LineMessagingProcessor.Workflows.csproj` | 14 | `7EA811F946FF092980D27825D6CBF174EFF32CC89C624F38139F9193A728B6CB` | project definition |

## Subject Tests

| Path | Lines | SHA-256 |
| --- | ---: | --- |
| `LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs` | 569 | `AEC8AEFA121835362FA4224BDA969C7CCCD2850754C25CC9252727D4A69E0005` |
| `LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj` | 25 | `62F32E9A454A902167F60977EBDB72ADB8EFB41E3C43C96E670E44155E4640B7` |

The test file covers product-friendly factories, selected factory guards,
notification success, blank recipient rejection, multi-user rejection,
retry-key pass-through, and provider rejection
(`LineNotificationWorkflowTests.cs:27-535`).

No F06 test references `LineReplyWorkflow`, `ReplyAsync`, or
`ReplyOrThrowAsync`. Coverage is absent for:

- reply success and every reply failure class;
- maximum-five and null-element message validation;
- recipient kind/ID consistency;
- retry-key format and accepted-duplicate/ambiguous outcomes;
- cancellation propagation and caller-cancellation classification;
- result sanitization and immutable snapshots.

## Read-Only Dependency Evidence

- F05A validates only nonblank recipient/token and nonempty message lists, then
  makes one provider call
  (`LineMessagingProcessor/LineMessagingProcessorClass.cs:317-351`).
- F04 documents maximum five messages
  (`Line.Messaging/ILineMessagingClient.cs:34-35,58-67`) and serializes one
  request per push/reply operation
  (`Line.Messaging/LineMessagingClient.cs:432-437,559-565`).
- F04 adds any nonblank retry string without format validation
  (`Line.Messaging/LineMessagingClient.cs:167-180`).

## Read-Only Consumer Evidence

- B05 creates a colon-delimited payment retry key
  (`SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:78-96`)
  and passes it into F06 (`:113-128`).
- The same consumer logs failed notification exceptions together with the LINE
  ID and retry key (`PaymentNotificationService.cs:130-135`).

These consumers prove reachability only. Their CRM, payment, logging, and
composition decisions remain outside F06 ownership.

## Gate State

The map permits analysis and diagnosis but requires a green F04/F05A provider
baseline and F05B/B04C/B05/B07 consumer gates before optimization
(`module-boundaries-and-optimization-map.md:829-830,877-883`).

No restore, build, test, package, generation, formatting, migration, benchmark,
coverage, or external LINE call was run.
