# B06C Extraction And Acceleration Analysis

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Findings

### B06C-EXT-001 Register needs a narrow credential-registration service boundary

- Evidence:
  - `RegisterManager` constructs `RegisterConnector` directly.
  - `RegisterConnector` owns validation, contact matching, eligibility checks, account uniqueness checks, and CRM writes in one class.
  - Credential creation overlaps B01 authentication while CRM contact lookup overlaps F03A and B02.
- Impact: B06C behavior cannot be tested or optimized independently because registration logic is bound to concrete CRM helpers and password persistence.
- Extraction contract:
  - Input: full name, mobile, account, password or credential command.
  - Output: typed register result code plus user-facing message.
  - Dependencies: B01 credential service, F03A contact/list lookup, B02 contact identity.
  - Test seam: fake contact/list/account stores.
- Status: confirmed extraction candidate.
- Recommended action: define `IChurchRegisterService` or equivalent B06C service, with B01 owning credential hashing and F03A/B02 owning CRM access contracts.

### B06C-EXT-002 Qualification read/write should not share mutable `LineBindingViewModel` state as the service boundary

- Evidence:
  - `HomeController.GetQualificationData` gets `InMemoryContext.LineBindingViewModel`, mutates it with CRM-loaded values, and returns JSON.
  - `HomeController.SaveQualificationData` gets the same view model object, mutates it from the posted model, then calls `UpdateContactInfomation`.
  - `LineBindingViewModel` constructs `ToolUtilityClass` directly.
- Impact: request behavior, CRM access, UI model state, and service operations are coupled. This makes identity validation, test setup, and extraction harder.
- Extraction contract:
  - Input: trusted contact identity plus qualification DTO.
  - Output: qualification DTO or save result.
  - Dependencies: B01 trusted identity, B02 contact profile service, B06A reference option lists.
  - Test seam: fake qualification repository/contact service.
- Status: confirmed extraction candidate with security impact.
- Recommended action: move qualification CRM operations behind a service that accepts a trusted identity and returns typed DTOs, leaving `LineBindingViewModel` as a presentation model only.

### B06C-EXT-003 Church hierarchy is currently consumed through ListManagement and should be documented as a B06A/B06C contract

- Evidence:
  - The B06C map row includes church hierarchy as primary scope.
  - Current route compatibility redirects `/Home/ChurchRoot` to `ListManagement.ChurchRoot`.
  - `LoadChurchRoot` lives in ListManagement, which is B06A reference/list ownership context.
- Impact: without a documented B06A provider and B06C consumer contract, future optimization may move hierarchy code into the wrong leaf or duplicate list/reference behavior.
- Extraction contract:
  - Provider: B06A list/reference data for hierarchy structure.
  - Consumer: B06C church hierarchy/register workflows that need hierarchy display or eligibility context.
  - Contract fields: area leader, race leader, group/member identifiers, display names, and paging shape.
- Status: confirmed documentation/extraction need.
- Recommended action: document the hierarchy contract before moving code; keep small-group reporting out of B06C ownership.
