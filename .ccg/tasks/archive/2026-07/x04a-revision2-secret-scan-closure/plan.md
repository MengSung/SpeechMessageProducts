# X04A Revision 2 Implementation Plan

## Task 1: Freeze Revision 2 Contract

- Update the X04A `wave_2` plans, measurements, and goals to Revision 2.
- Limit product/test writes to `appsettings.json` and
  `RuntimeConfigurationSecretScanTests.cs`.
- Record original `0/21`, legacy `6/6`, and comments `3` baselines.
- Update the global blueprint from `COMMITTED` to Revision 2 contract review.
- Run Claude-only contract review; record no-output truthfully if unavailable.

## Task 2: Add Red Scanner Cases

- Add the exact six legacy alias paths without changing the original 21 paths.
- Add a raw-comment scanner that emits only line/key/category.
- Add synthetic fixtures for one active value, one legacy alias, and one
  commented assignment.
- Run only `RuntimeConfigurationSecretScanTests`; expected pre-repair result is
  failure with counts `LegacyAliasLiteralCount=6/6` and
  `CommentedSensitiveLiteralCount=3`.

## Task 3: Clear Residual Literals

- Clear the six legacy Sandbox values in `appsettings.json`.
- Remove the three commented sensitive assignments.
- Preserve the Sandbox section, endpoint values, and all non-secret metadata.
- Run the scanner tests again; expected result is `0/21`, `0/6`, and comments
  `0`.

## Task 4: Full Verification

- Run all four focused X04A test classes.
- Build `SpeechMessageProducts.ChurchReport.csproj` with `--no-restore`.
- Run key/path-only source scans, `git diff --check`, and exact allowlist check.
- Confirm no secret value appears in test, task, review, or wave evidence.

## Task 5: Review And Commit

- Run Claude-only diff review through the self-healing entrypoint.
- Apply valid findings and repeat verification.
- If Claude has no usable output, record the degraded state and perform the
  platform-permitted inline verification without claiming external approval.
- Commit with a Traditional Chinese subject/body and archive this CCG task only
  after the blueprint returns X04A to `COMMITTED`.
