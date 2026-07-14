# B06C Security Analysis

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Findings

### B06C-SEC-001 Register writes plaintext application passwords to CRM contact fields

- Evidence:
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:132` writes `Password` into `new_app_pass` when the account already belongs to the matched contact.
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs:146` writes `Password` into `new_app_pass` for new account setup.
  - `SpeechMessageProducts.ChurchReport/Models/RegisterManager.cs:27-31` simply delegates raw register form values into `RegisterConnector.Register`.
  - `SpeechMessageProducts.ChurchReport/Views/Home/Register.cshtml:20` posts the register form to `Home.ProcessRegister`.
- Impact: raw password storage expands compromise impact to CRM reads, diagnostics, backups, and administrator access paths. This is a security issue even if the route is otherwise protected.
- Boundary: B06C owns the register workflow and should not hand this risk to B01; B01 owns authentication/session contracts, but B06C currently creates the credential material.
- Status: confirmed static issue.
- Recommended action: replace plaintext storage with a B01-owned credential hashing/verification contract or retire this legacy credential path behind a migration plan.
- Validation: inspect authentication readers for `new_app_pass`, add a migration-compatible test for hash creation/verification, and prove existing users can transition without exposing raw passwords.

### B06C-SEC-002 Qualification endpoints trust caller-supplied LINE user id for CRM PII reads/writes

- Evidence:
  - `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:565-576` accepts `UserLineId` from POST data and uses it to load contact faith status, gender, birth date, and personal id.
  - `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:605-622` accepts a bound `LineBindingViewModel`, copies fields including `PersonalId`, and updates CRM by `LineUserId`.
  - `SpeechMessageProducts.ChurchReport/Views/Home/QualificationView.cshtml:789-807` obtains `profile.userId` client-side and then sends it to `GetQualificationData`.
  - `SpeechMessageProducts.ChurchReport/Views/Home/QualificationView.cshtml:830-838` posts `UserLineId` directly from JavaScript.
- Impact: unless upstream LIFF/login/session validation binds the posted LINE user id to the authenticated principal, a caller can attempt contact PII access or mutation by changing the POST payload.
- Boundary: B01 owns the identity/session contract, but B06C owns the qualification endpoint behavior and must require a trusted identity source or explicit guard.
- Status: confirmed static risk; runtime validation needed to prove whether global filters and LIFF/session binding already block tampering.
- Recommended action: make qualification handlers derive LINE user id from a trusted server-side identity/session source, or validate posted values against B01 before CRM reads/writes.
- Validation: add negative tests or runtime checks showing a mismatched posted `UserLineId` cannot read or update another contact.

### B06C-SEC-003 Register and qualification mutation paths need CSRF/anti-forgery proof

- Evidence:
  - `SpeechMessageProducts.ChurchReport/Views/Home/Register.cshtml:20` posts a form to `ProcessRegister`.
  - `SpeechMessageProducts.ChurchReport/Views/Home/QualificationView.cshtml:203` posts a form to `SaveQualificationData`.
  - `SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs:603-605` marks `SaveQualificationData` as an HTTP POST mutating endpoint.
  - Static slices reviewed here did not show action-level anti-forgery attributes on the B06C endpoints.
- Impact: cross-site form submission could mutate credential or qualification data if global anti-forgery coverage is absent or incomplete.
- Status: hypothesis; requires route/filter validation because global MVC filters may provide protection.
- Recommended action: document or add anti-forgery coverage for B06C POST routes.
- Validation: runtime request tests should reject missing/invalid anti-forgery tokens where browser form cookies are accepted.

## Rejected Security Candidates

- Small-group weekly reporting authorization is excluded from B06C except for register eligibility checks.
- Fee/payment transaction security is excluded and remains B05/F08/F09 responsibility.
