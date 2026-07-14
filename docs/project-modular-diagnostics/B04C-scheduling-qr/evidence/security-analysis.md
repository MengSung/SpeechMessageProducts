# B04C Security Analysis

## QR Replay And Session-Crossing Risk

`QrCodeController` stores incoming `QrCodeId` into `InMemoryContext.ListManager.QrCodeId` in QR view actions. Later POST handlers read the QR id back from that context while accepting `UserLineId`, display name, group id, room id, and view type from the browser payload. The inspected files do not show a signed QR token, expiry, nonce, replay cache, idempotency key, or server-side verification that the posted user id belongs to the LIFF user that scanned the code.

Relevant evidence:

- QrCode view writes `InMemoryContext.ListManager.QrCodeId`: `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:64`.
- Poll view writes the same context field: `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:144`.
- POST handlers accept browser-posted identity fields: `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:83`, `:163`, and corresponding small-group/Sunday/personal handlers in the same file.
- QR utility invocation uses the context QR id plus posted user id: `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:103`.
- Poll save uses context QR id and context line user id: `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs:206-209`.

Security impact: state-changing QR operations can be replayed or applied against stale/cross-request context if an attacker can forge or replay browser AJAX payloads or if scoped context does not isolate the QR id exactly as expected for every scan path.

## Scheduler Mutation Authorization Gap

`SchedulerDataController` directly exposes `Get`, `Post`, `Put`, and `Delete`. The controller file does not show `[Authorize]`, anti-forgery validation, model validation, ownership checks, or a B01 policy. `Post` and `Put` have commented-out model validation. `Put` and `Delete` mutate by client-supplied key.

Relevant evidence:

- Controller derives from `Controller`: `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:29`.
- Read endpoint returns loaded appointments: `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:41-44`.
- Post deserializes raw `values` and appends: `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:47-59`.
- Put finds by key and populates raw values: `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:62-73`.
- Delete finds by key and removes: `SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs:77-82`.

Security impact: if these routes are reachable without global auth enforcement, appointment data can be read or mutated by an unauthorized caller. Even with global auth, ownership and validation remain missing in the action surface.

## Recommended Controls

- Add a B04C QR token verifier with signed payload, expiry, nonce, action type, and replay protection.
- Bind QR POSTs to server-verified LINE identity, not only `UserLineId` posted from the client.
- Move QR target authority out of mutable `ListManager.QrCodeId` and into the signed token validation result.
- Require explicit authorization and ownership checks on scheduler mutation endpoints.
- Add anti-forgery or API-specific CSRF protection for browser-origin scheduler mutations.
- Add idempotency checks for attendance and poll writes.

## Runtime Validation Needed

- Confirm whether middleware or filters globally protect `SchedulerDataController` routes.
- Confirm whether LIFF id token verification exists outside the inspected view/controller files.
- Confirm lifetime of `IInMemoryDataContext` under concurrent LIFF scans.
