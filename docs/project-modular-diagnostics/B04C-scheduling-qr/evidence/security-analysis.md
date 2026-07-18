# B04C Security Analysis

## Summary

One Critical security issue needs immediate handling. B04C QR scan endpoints accept caller-supplied LINE identity fields from browser JavaScript and immediately use the supplied LINE user id to look up contacts and mutate attendance, present-record, course, small-group, and Sunday QR records. The client does call LIFF `getProfile()`, but the server-side B04C actions do not receive or validate a LINE id token, access token, nonce, audience, issuer, or signature before trusting `UserLineId`.

## Findings

### B04C-SEC-001 QR scan endpoints trust caller-supplied LINE user id for attendance mutations

- Evidence:
  - QR views load the LIFF SDK and call `liff.getProfile()`, then POST `DisplayName`, `UserLineId`, `GroupId`, `RoomId`, and `ViewType` to B04C endpoints without sending server-verifiable LINE proof at SpeechMessageProducts.ChurchReport/Views/QrCode/QrCodeView.cshtml:59-142, PersonalQrCodeView.cshtml:60-147, SmallGroupQrCodeView.cshtml:59-139, and SundayQrCodeView.cshtml:60-142.
  - `QrCodeGetLineId`, `SmallGroupQrCodeGetLineId`, `SundayQrCodeGetLineId`, and `PersonalQrCodeGetLineId` accept those POST parameters directly and pass `UserLineId` to QR utilities at SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:83-110, 252-276, 327-353, and 405-431.
  - `SetupLineContext` stores the caller-supplied LINE context into in-memory/session-facing objects at SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:470-496.
  - QR utilities resolve CRM contacts by the supplied LINE user id and then update CRM attendance/QR records at SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs:160-190 and 337-345, SmallGroupQrCodeUtility.cs:157-209 and 221-250, SundayQrCodeUtility.cs:147-223 and 388-419, and PersonalQrCodeUtility.cs:150-260.
- Control/data/lifetime flow:
  - Browser POST body `UserLineId` -> B04C controller -> B04C session/line context -> QR utility contact lookup -> CRM present-record/course/weekly-report update.
- Impact:
  - A caller who can reach the QR POST endpoints can attempt to scan as another LINE user id and write attendance or QR scan state under that contact.
  - Small-group QR has an additional side effect: if the LINE contact is not found, it can create or connect friend/contact/member-list records before signing at SmallGroupQrCodeUtility.cs:165-172 and 243-250.
  - Sunday/personal QR scans can create present records and mark attendance-like fields in downstream B04A records without a server-side proof that the request subject is the LINE subject.
- Why this is necessary:
  - LIFF profile data is client-observed identity material, not an authorization proof for the server. B04C must validate a LINE-issued token or call a trusted server-side verification path before any CRM write.
- Recommended immediate handling:
  - Require a LINE id token or access token proof on each QR POST and validate issuer, audience/channel, subject, expiry, and nonce server-side before using `UserLineId`.
  - Reject mismatches between validated LINE subject and posted `UserLineId`.
  - Make QR writes idempotent by validated subject + QR id + scan type, and log rejected forged attempts without exposing tokens or PII.
  - Add tests for forged `UserLineId`, cross-user scan, missing token, token/user mismatch, and replayed QR POST.

### Scheduler mutation endpoint ownership guard is weak but not promoted as separate Critical

- Evidence:
  - SchedulerDataController exposes `Post`, `Put`, and `Delete` methods that populate appointment objects from request `values` and mutate `_data.Appointments` at SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:47-83.
  - The backing `InMemoryAppointmentsDataContext` caches appointment collections by session id at SpeechMessageProducts.ChurchReport/Models/InMemoryAppointmentsDataContext.cs:34-53.
  - Appointment CRM update/delete dependency code updates or deletes by appointment id at SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:682-740.
- Assessment:
  - This is a B04C/B04B boundary risk rather than a separate confirmed B04C Critical because the inspected SchedulerDataController writes only the session memory collection in this path, while the CRM persistence details live in B04B dependency context.
  - It should be addressed as part of a scheduler command boundary with per-record ownership checks.

## Non-Issues / Deferred

- No hardcoded LINE token literal was found in the scoped B04C QR utilities; tokens are read from configuration.
- CSRF is plausible for QR POST and scheduler mutation endpoints, but global antiforgery/session policy belongs to B01/X01. B04C should still require server-side LINE proof because antiforgery alone does not authenticate the LINE subject.
- LINE SDK transport internals and reusable notification workflow are F04/F06/B07 context and are not diagnosed here.

