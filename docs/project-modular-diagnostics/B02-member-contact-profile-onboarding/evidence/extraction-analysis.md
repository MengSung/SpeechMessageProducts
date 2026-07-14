# B02 Extraction And Acceleration Analysis

## Confirmed Extraction Findings

### Avatar and contact update policy should be extracted behind a shared B02 service

Evidence:

- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:271` and `PersonalController.ImageUpload.cs:498` both implement single contact image retrieval.
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:344` and `PersonalController.ImageUpload.cs:661` both implement batch image retrieval.
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:747` and `PersonalController.ImageUpload.cs:90` both upload contact images.
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:822`, `PersonalController.cs:914`, and `PersonalController.cs:1164` all mutate contact profile fields through separate controller logic.
- Guard behavior diverges: `MemberInfoController.cs:277`, `375`, `757`, and `830` guard contact access; Personal image/update paths do not use an equivalent guard.

Assessment:

- This is not a purely aesthetic refactor. Duplication has already produced a security policy mismatch.
- Extraction should follow route-preserving application-service boundaries rather than moving code across modules.

Recommended contract:

- `IContactProfileScopeService`
  - Input: current user/session context and requested contact ids.
  - Output: allowed contact id set plus denied ids.
- `IContactAvatarService`
  - Input: authorized contact id(s), size/fit options.
  - Output: image bytes/data URL/source and cache metadata.
- `IContactProfileUpdateService`
  - Input: authorized contact id(s) and update command.
  - Output: per-contact success/failure results.

### OptionSet mapping should be centralized for B02 contact profile fields

Evidence:

- `PersonalController.cs:580-593` already centralizes option-set service construction with shared cache.
- `ContactService.cs:299-328`, `NewPerson.cs:583-701`, and `PersonalInfomatioManager.cs:311-915` re-create the same service/cache pattern for contact option fields.

Assessment:

- Extracting a B02 contact option mapper would reduce CRM metadata calls and remove repeated fallback/default handling.
- This is a low-risk acceleration step after security fixes.

## Rejected Extraction Candidates

- Splitting all of `MemberInfoController.cs` solely because it is large: rejected as too broad for diagnosis. Retained extraction candidates are tied to concrete guard/performance drift.
- Moving B02 CRM connector code into F03A immediately: rejected. B02 owns business-specific member/contact mappings; F03A owns generic CRM operations.
- Moving shared Razor/DevExtreme UI into X03 immediately: rejected for this pass. Route and policy fixes should happen first, then X03 can consume stable B02 endpoints.
