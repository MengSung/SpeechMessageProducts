# Verification Record

## Source Branch

### Passing gates

- `SpeechMessage.Dynamics.Tests`: 47 passed, 0 failed.
- `SpeechMessage.Dynamics.SmokeTests`: 4 passed, 0 failed; live CRM access remains disabled by default.
- `SpeechMessageProducts.ChurchReport` Release build: passed with 0 errors.
- `SpeechMessage.Dynamics.Gateway` Release build: passed with 0 warnings and 0 errors.
- `SpeechMessage.Dynamics.ProductClient` Release build: passed with 0 warnings and 0 errors.
- Product/test/config diff whitespace check excluding committed CCG/Trellis/docs evidence: passed.
- Merge preview (`git merge-tree --write-tree`): passed with no conflicts.
- Current `appsettings.json` password values classify as placeholders/references; the historical plaintext value mentioned by Gemini was redacted from the new analyzer artifact.

### Baseline failures, not introduced by source

The full solution command completed builds but returned failure because:

- `ChurchReport.MemberInfo.Tests`: 22 failed / 304 passed. The same 22 failures occur on target tip `82df2440e`; failures include tests that still search for the removed `ChurchReport.sln` name and payment-refactor expectations not present on either branch.
- `LineMessagingProcessor.RichMenus.Tests`: 1 failed / 33 passed. The same boundary test fails on the target tip because it cannot locate the old solution root.
- `ToolUtility.Tests` cannot restore because it targets `net8.0` while the referenced `ToolUtility` project targets `net10.0`. The identical `NU1201` failure occurs on the target tip, and this source branch does not modify either project target framework.

These failures are an identical pre-merge target baseline, not additional failures introduced by the source branch. They remain repository debt and prevent claiming that the repository-wide test suite is fully green.

### Non-blocking warnings

- The report-only no-Dynamics-SDK scanner exited successfully and reported 1,069 legacy findings, consistent with the Phase 0 inventory mode.
- Restore/build reported existing `NU1903` high-severity advisories for `System.Security.Cryptography.Xml` 10.0.9 through `ToolUtility` and `PowerPlatform.Dataverse.Client`. The source branch does not change the affected package references.
