# P4-04 金鑰改為具名設定檔

## Goal

Follow 04A §5 P4-04 exactly: validate named Sinopac profiles, add church-owned profile lookup, support deployment activePhase 4 for new_sinopacprofile only, and do not connect to D365.

## Requirements

- Validate a requested named payment profile fail-closed; a missing profile or blank name without `DefaultProfile` returns `ConfigurationInvalid`.
- Validate required Sinopac profile credentials `ShopNo`, `A1`, `A2`, `B1`, `B2`, `XKeyId`, and `Endpoints:ApiBaseUrl`. Error text may contain the profile name and missing key names, never configured values.
- Add `ChurchPaymentProfile.Read` to read only `account.new_sinopacprofile`, accept 1-64 ASCII letters, digits, underscore, hyphen, or period, and reject blank or invalid names with `InvalidOperationException`. Do not disclose church names or personal data and do not fall back to a default profile.
- Preserve church isolation: resolve the profile using the server-owned church/account ID; never use caller-provided or payload profile names as authority. Do not add shared mutable profile state or a cross-church cache.
- Add Deployment `ActivePhase`, defaulting to 1; parse top-level `activePhase` in the range 1-8. Include schema phases up to `ActivePhase` in planning and deployment, while keeping primary-name and key-field phase-1 restrictions unchanged.
- Set the official schema's `activePhase` to 4. With a phase-1-complete snapshot and `ActivePhase=4`, the plan must contain exactly one `CreateAttribute` for `account.new_sinopacprofile` and its matching `IncludeAttribute`, with no `Update` or `Delete`.
- Retain the existing phase-1 plan contract by making `Plan_OnlyBaseEntities_ContainsOnlyRequiredP1Additions` explicitly use `ActivePhase=1` without changing its assertions.
- Use the approved profile path `Payment:Profiles` (D4-1). Do not modify ChurchReport or its appsettings files, touch existing keys, add a `.csproj`, or connect to D365.

## Acceptance Criteria

- [ ] `Profile_Missing_FailsClosed` and `SinopacProfile_MissingCredential_FailsValidation` exist and pass.
- [ ] `ChurchPaymentProfile.Read` is implemented and `Profile_TenantA_CannotUseTenantBProfile` passes for both target frameworks, including 100 interleaved A/B reads and a missing church ID.
- [ ] `Source_NoHardcodedKeys` exists and passes using the exact 04A scan exclusions and patterns.
- [ ] `Plan_ActivePhase4_AddsSinopacProfileOnly` exists and proves exactly one create/include pair for `account.new_sinopacprofile`, with no update or delete; the phase-1 test remains unchanged in behavior.
- [ ] Run `dotnet test SpeechMessage.Dynamics.Deployment.Tests -c Release`, `dotnet test SpeechMessageProducts.GoodShepherd.Core.Tests -c Release --filter "FullyQualifiedName~ChurchPaymentProfile"`, and `dotnet test SpeechMessage.Payments.Tests -c Release`; all specified tests exist by their exact names and pass.
- [ ] Full solution Release build has zero errors; no D365 connection or write occurs.
- [ ] Changed source documentation and byte-level encoding checks pass; fixed `C:\smv\guard` §8.1 passes after the code commit.
- [ ] The one-page P4-04 report and progress DATA are complete; after acceptance, push the P4 branch and verify its remote SHA with `git ls-remote`.

## Constraints

- Work only within the P4-04 files and behaviors listed in 04A §5. Do not implement P4-03, create the Dataverse attribute live, or alter ChurchReport configuration.
- Existing settings remain secret-bearing inputs. Tests use synthetic credentials and assert only safe key-name diagnostics.
- Schema planning and tests are offline with synthetic metadata; live D365 structure work is reserved for P4-06.
