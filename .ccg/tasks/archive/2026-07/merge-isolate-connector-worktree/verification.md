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

## Merged Target Branch

- Merge commit: `f9e544e05d37b5a56b8816d8d11da175a311e85a`.
- Merge parents: target tip `82df2440e17708172ee4706c5f54d2932e569e7a` and source audit tip `33f4aa9a7cb70a22d8581648639f41f3f7de33ad`.
- Source audit tip is an ancestor of the merged target.
- `SpeechMessage.Dynamics.Tests`: 47 passed, 0 failed after merge.
- `SpeechMessage.Dynamics.SmokeTests`: 4 passed, 0 failed after merge.
- ChurchReport, Dynamics Gateway, and Dynamics ProductClient Release builds passed after restore.
- Full solution result after merge remained identical to the pre-merge target baseline: 22 ChurchReport.MemberInfo failures and 1 RichMenus boundary failure; no additional failures appeared.
- A source-added Dynamics test fixture still used a known historical credential string as negative test data. It was replaced with `unit-test-plaintext-password`; the focused test and full Dynamics test project then passed.
- Cleanup verification: the source tip was confirmed as an ancestor of the target, the worktree registration was removed, and local branch `1.0.0.2.IsolateConnector.Worktree` was deleted. `origin/1.0.0.2.IsolateConnector.Worktree` remains unchanged. The former worktree path is empty but could not be removed while the active Codex workspace held it open.
