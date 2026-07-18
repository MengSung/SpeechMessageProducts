# X04A Revision 2 Requirements

## Goal

Close the residual `X04A-SEC-001` evidence gap without expanding Wave 2 to a
new canonical issue.

## Confirmed Gap

- The current test scans only the frozen 21 active JSON paths and skips source
  comments.
- Three commented sensitive literals remain in tracked configuration.
- Six non-empty legacy Sandbox credential aliases remain outside the frozen
  manifest; five match values removed from the primary test profile.
- No secret or credential value may be written to task, review, test output, or
  durable evidence.

## Required Outcome

- Revise the X04A Wave 2 contract before product edits.
- Scan both active JSON paths and raw-source comments using key/path-only
  diagnostics.
- Clear the six legacy Sandbox credential aliases and remove the three
  commented sensitive assignments while preserving section structure and
  non-secret metadata.
- Keep Production validation, host configuration bridging, and all 13 migrated
  consumers unchanged.
- Run focused X04A tests, ChurchReport build, secret-pattern checks, allowlist
  verification, and review before one Traditional Chinese commit.

## Scope Boundary

Allowed product/test paths are limited to:

- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationSecretScanTests.cs`

The three X04A Wave 2 contract files and global orchestration records may be
updated as planning and evidence artifacts. No other product or test path is
authorized.
