# B02 Performance Analysis

## Confirmed Performance Findings

### Maintain-profile save launches unbounded background CRM work

Evidence:

- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:909-910` comments describe fire-and-forget behavior.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:971` starts `_ = Task.Run(() =>`.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:983` loops submitted members.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1004` retrieves each CRM contact.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1079` updates CRM inside the background loop.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1128-1129` traces background failures after response.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:1139-1140` returns success while background processing is still running.
- `SpeechMessageProducts.ChurchReport/Views/Personal/MaintainPersonInfomationView.cshtml:143` uses a 3000 ms AJAX timeout for the save call.

Assessment:

- This avoids request timeout at the cost of unbounded thread-pool work and hidden partial failures.
- A bounded awaited batch or durable job abstraction is needed before optimization.

### OptionSet metadata cache is defeated in legacy contact/onboarding paths

Evidence:

- `SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:36` sets a 24-hour cache duration.
- `SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:67-70` returns cached mappings when present.
- `SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:76-83` performs CRM metadata retrieval on cache miss.
- `SpeechMessageProducts.ChurchReport/Services/OptionSetMetadataService.cs:106-109` stores the mapping in the cache.
- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs:580-593` shows a better local pattern: create one service backed by injected `IMemoryCache`.
- `SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:299-304` creates a fresh `MemoryCache` for one mapping.
- `SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:321-326` creates another fresh `MemoryCache`.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs:583-588`, `NewPerson.cs:624-629`, and `NewPerson.cs:696-701` repeat the pattern.
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs:311-316`, `353-358`, `724-729`, `753-758`, `779-784`, `869-874`, and `910-915` repeat the pattern.

Assessment:

- Recreating cache instances at conversion sites means repeated requests cannot share metadata cached for 24 hours.
- The issue is confirmed by code shape. Runtime validation should quantify saved CRM metadata calls before optimization.

## Lower Priority Observations

- `PersonalController.cs:41-42` has a static fallback cache for option metadata. This is not a problem by itself because the normal path prefers injected `IMemoryCache`.
- `MemberInfoController.cs:585` creates a short-lived `HttpClient` for profile image probing during resync. It is bounded by a 4-second timeout and admin-only flow, so it is not retained as a confirmed high-value performance issue.
- `MemberInfoController` and `PersonalController.ImageUpload` batch image endpoints do not cap request contact-id count. This can amplify memory/CPU because they return base64 images, but the stronger retained issues are authorization and extraction. A cap should be considered when fixing B02-SEC-003.
