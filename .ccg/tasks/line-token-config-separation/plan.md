# ChurchReport LINE Token Configuration Separation

**Goal:** Keep LINE channel access tokens out of tracked settings while preserving isolated Development and production configuration paths.

**Architecture:** Keep Development settings on the existing base JSON, Development JSON, and User Secrets path, and verify that any configured DevTest token is supplied by the User Secrets provider. Add a `LINE__`-prefixed environment provider only outside Development so all ChurchReport configuration builders can resolve production channel aliases without storing token values in repo files.

**Files:** `SpeechMessageProducts.ChurchReport/appsettings.json`, `SpeechMessageProducts.ChurchReport/Configuration/ChurchReportDevelopmentConfiguration.cs`, `SpeechMessageProducts.ChurchReport/Program.cs`, `LineMessagingProcessor/LineMessagingProcessorClass.cs`, `ChurchReport.MemberInfo.Tests/Contracts/DevelopmentSafetyTests.cs`, `ChurchReport.MemberInfo.Tests/Contracts/DevelopmentOrganizationSettingsTests.cs`, `.trellis/spec/backend/development-configuration-safety.md`.

## Ordered Work

- [ ] Add tests that confirm tracked base LINE token fields are empty without exposing configured values.
- [ ] Add a test proving Development User Secrets can supply the DevTest token and that the real source is the test-owned User Secrets provider.
- [ ] Add tests proving Development rejects nonempty production aliases, generic LINE token fallback, and DevTest values supplied by another provider.
- [ ] Run the targeted test project and confirm the new contract tests fail for the intended reasons.
- [ ] Clear only the two base JSON token values through a JSON-aware edit.
- [ ] Add the `LINE__` environment provider to non-Development ChurchReport configuration builders and the host configuration before startup validation; keep Development provider order unchanged.
- [ ] Verify all existing ChurchReport LINE config builders use the shared environment override and the shared processor remains independent of User Secrets.
- [ ] Update the Development configuration safety spec with production variable names, Development source restrictions, and no-secret test requirements.
- [ ] Run targeted tests, ChurchReport build, encoding and diff checks; do not run D365 or LINE operations.
