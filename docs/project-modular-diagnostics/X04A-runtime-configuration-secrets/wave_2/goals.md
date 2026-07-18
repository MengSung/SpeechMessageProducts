# Wave 2 Revision 2 Goals: X04A Residual Secrets

- Wave: Wave 2
- Revision: 2
- Canonical issue: `X04A-SEC-001`
- Contract status: `CONTRACT_STATUS: CONTRACT_REVISION_APPROVED_DEGRADED`

This file is the completion authority for Revision 2. Targets may not be
weakened after repair begins.

## Success Targets

All targets must hold simultaneously:

```text
OriginalManifestLiteralCount=0/21
LegacyAliasLiteralCount=0/6
CommentedSensitiveLiteralCount=0
ScannerDetectedCases=3/3
ScannerDisclosedFixtureValues=0/3
```

The focused X04A suite passes, ChurchReport builds with zero errors,
`git diff --check` is clean, and the repair changes exactly the two product/test
paths in `plans.md`.

## Required Preserved Behavior

- Original configuration key paths and non-secret metadata remain available.
- The Sandbox section and endpoint configuration remain present.
- Runtime bridge initialization, all 13 consumer migrations, and Production
  validation behavior remain unchanged.
- Development remains free of the Production-only startup gate.
- No test, error, task, measurement, prompt, or review artifact contains a
  credential or synthetic fixture value.

## Revision 2 Execution Evidence

- Evidence time: `2026-07-18T02:40:43Z`.
- Original manifest remained `0/21`.
- Legacy aliases changed from `6/6` non-empty to `0/6` non-empty.
- Commented sensitive assignments changed from `3` to `0`.
- Scanner cases passed `5/5`; the focused X04A suite passed `36/36`.
- ChurchReport built with `0` warnings and `0` errors.
- The product/test allowlist matched `2/2` paths, with no unexpected or
  missing path; `git diff --check` passed.
- UTF-8 without BOM and CRLF were retained for both product/test files.

Claude-only run `20260718-103104-x04a-revision2-final-reviewer` completed two
healthy attempts but returned no usable findings (`completedBackends=[]`). It
is not external approval. The permitted inline, value-free review found no
unresolved Critical or Warning. Execution state is
`VALIDATED_AWAITING_COMMIT`.

## Failure Conditions

Revision 2 fails if any of the following occurs:

- an original manifest or legacy alias path remains non-empty;
- a commented sensitive assignment remains;
- the scanner misses a synthetic class or returns a matched value;
- any Revision 1 product path changes;
- a key path, Sandbox section, endpoint, or non-secret setting is removed;
- focused tests/build fail or the diff exceeds the two-path allowlist;
- review has an unresolved Critical or Warning finding.

## Review And Commit Gate

Claude-only review is attempted through the self-healing runner. A no-output run
is recorded as unavailable, not approval. Gemini is prohibited. The active
execution platform's permitted local/fallback review gate must find no unresolved
Critical or Warning before the repair commit.

One Traditional Chinese commit records the baseline, final counts, validation,
review state, and rollback boundary. X04A returns to `COMMITTED` only after that
commit is independently checked against the two-path allowlist.

## Rollback

Revert the Revision 2 repair as one unit only when operationally required, but
never restore removed literals. Managed external configuration remains the only
source for runtime credentials.

## Revision 1 Preserved Completion

`ab9993e8` remains committed for `X04A-SEC-002` and `X04A-PERF-001`. Revision 2
must preserve its `0/8` unsafe controls, `13/13` bridge consumers, `0/13` local
builders, and `4/4` lifecycle results.
