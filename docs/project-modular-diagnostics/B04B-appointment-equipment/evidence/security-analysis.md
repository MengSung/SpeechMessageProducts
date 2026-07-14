# B04B Security Analysis

## Summary

One Critical security issue is identified in the appointment LINE binding path. The endpoint accepts caller-supplied identity material, stores it in session, and issues an authentication cookie without visible proof that the supplied LINE user id came from a trusted LIFF/OAuth verification step inside the B04B flow.

## Findings

### B04B-SEC-001 Caller-supplied LINE user id can become session and auth-ticket identity

- Evidence:
  - `AppointmentController.LoadAppointmentByLineId` is an HTTP POST accepting `UserLineId`, `GroupId`, `RoomId`, and `ViewType` from the request at SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:134-139.
  - The action writes those request values into `LineBindingViewModel` and `AppointmentsListManager` at SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:158-179.
  - `SetupAppointmentAccountPasswordAsync` then sets `m_Account = "LineIdLogin"`, `m_Password = lineUserId`, `_LoginPassword`, `_SessionUserId`, and calls `IssueAuthTicketAsync(..., "LineIdLogin", lineUserId, "LINE")` at SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:185-193.
  - `BaseChurchController.IssueAuthTicketAsync` builds and signs an authentication cookie using the supplied `passwordKey` at SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:1007-1016.
  - Downstream appointment/equipment login resolution treats `"LineIdLogin"` password as a LINE user id at SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:125-135 and SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs:101-111.
- Impact:
  - If the endpoint is reachable by a caller with any valid session, or if global authorization is configured to allow session fallback, a request can pivot the session/auth ticket to an arbitrary LINE user id.
  - Appointment reads and writes can then operate through `RetrieveContactEntityByLineUserId`, creating an identity/authorization boundary violation inside B04B.
- Existing guard context:
  - `GlobalAuthorizationFilter` is globally registered and blocks unauthenticated requests unless anonymous access or session fallback applies at SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs:23-39 and Startup.cs:376-389.
  - The B04B issue remains because the endpoint mints a new identity from request body data after authorization rather than verifying a LINE id token, LIFF access token, nonce, audience, issuer, or server-side binding.
- Recommended immediate handling:
  - Do not issue auth/session identity from `UserLineId` alone.
  - Require a trusted LINE/LIFF proof and bind it server-side before setting `_LoginPassword`, `_SessionUserId`, or issuing a cookie.
  - Add explicit tests for forged `UserLineId`, cross-user session pivot, and existing-session privilege swapping.

### Appointment mutation endpoints lack visible per-record ownership guard

- Evidence:
  - `PostAppointments`, `PutAppointments`, and `DeleteAppointments` directly call create/update/delete on `AppointmentsListManager` at SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:255-320.
  - `LoadAppointments` has `EnsureCorrectUserData()` commented out at SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:219-235.
  - `PutAppointments` and `DeleteAppointments` select appointments from the current in-memory list by `AppointmentId` before invoking CRM update/delete at SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs:284-320.
  - CRM update/delete resolves by appointment Guid at SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:682-740.
- Impact:
  - The in-memory list selection reduces direct arbitrary-id risk, but the absence of explicit per-record owner verification at mutation time leaves the flow dependent on session/list correctness.
  - This should be treated as a follow-on hardening item tied to B04B-SEC-001 rather than a separate confirmed Critical issue.

## Non-Issues / Deferred

- No hardcoded credential literal was found in the scoped B04B owner files during this pass.
- CSRF risk is plausible for POST/PUT/DELETE endpoints, but global cookie SameSite and framework-level antiforgery configuration require broader X01/B01 review. It is not promoted as a B04B confirmed issue here.
