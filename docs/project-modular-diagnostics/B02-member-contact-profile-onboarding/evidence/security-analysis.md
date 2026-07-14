# B02 Security Analysis

## Confirmed Security Findings

### Personal maintain profile updates lack object-level contact authorization

Evidence:

- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:914` exposes `SaveMaintainPersonInfomation(string aResult)`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:931` deserializes client JSON into `List<Member>`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:983` loops every submitted `member`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1004` retrieves `contact` by client-provided `member.ContactId`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1079` updates the CRM entity.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1164` exposes `[HttpPut] UpdateMaintainPersonInfomation`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1194` creates an update entity for the parsed `contactGuid`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1245` calls `toolUtility.UpdateEntity(entityToUpdate)`.
- Contrast: `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:830` rejects updates when `!CanViewContact(contactGuid)`.

Assessment:

- Authenticated route access is not enough. The update target is a contact object, and the object id is client controlled.
- The expected B02 policy already exists in `MemberInfo` but is not applied in `Personal`.

### Missing anti-forgery protection for B02 mutations

Evidence:

- `SpeechMessageProducts.ChurchReport/Startup.cs:377-389` registers MVC filters for theme, cache, and global authorization; inspection found no `AutoValidateAntiforgeryToken`.
- B02 mutating actions include `PersonalController.cs:888`, `PersonalController.cs:913`, `PersonalController.cs:1164`, `MemberInfoController.cs:747`, `MemberInfoController.cs:820`, and `NewPersonController.cs:345`.
- Raw AJAX callers include `Views/Personal/MaintainPersonInfomationView.cshtml:139`, `Views/MemberInfo/_MemberDetailPopup.cshtml:464`, and `Views/MemberInfo/_MemberDetailPopup.cshtml:545`.
- Search over B02 controllers/views found no `ValidateAntiForgeryToken`, `AutoValidateAntiforgeryToken`, `__RequestVerificationToken`, or `RequestVerificationToken` usage.

Assessment:

- Because these routes rely on cookie/session authentication, state-changing actions need anti-forgery validation.
- X01 may be the final owner for a global filter, but B02 owns the affected routes and must be included in validation.

### Personal avatar endpoints expose arbitrary contact images/LINE picture URLs

Evidence:

- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:498` exposes `GetContactImage(string contactId, int size = 80)`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:511-524` uses current login contact only when `contactId` is blank; any parseable supplied id becomes the target.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:540-544` retrieves CRM contact image fields for that id.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:566-571` redirects to the contact's LINE picture URL fallback.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:661` exposes batch retrieval.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs:685-724` parses arbitrary ids and retrieves them in one CRM query.
- Contrast: `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:277` checks `CanViewContact`; `MemberInfoController.cs:375` applies `CanViewContactsBatch`.

Assessment:

- This is object-level read disclosure of member avatar data and external LINE picture URLs.
- Cache lookups should occur after per-request authorization, not before.

## Rejected Security Candidates

- `MemberInfo` avatar endpoints: rejected because single and batch paths have explicit contact-scope guards.
- Upload file type/size bypass: not retained as confirmed because upload paths enforce 5 MB limits and image MIME/extension checks; image bomb runtime hardening remains a future test case.
- `GlobalAuthorizationFilter` session fallback: recorded as B01 dependency, not B02 issue for this module.
- Debug logs: not retained as confirmed PII leak because the inspected B02 timing/image paths use `Debug.WriteLine` and comments state they do not write production trace logs.
