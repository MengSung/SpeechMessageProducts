# F01B Wave 1 Execution Plan

## Contract gate

- [ ] Confirm the three `wave_1` documents contain exactly the five selected
  F01B IDs and pass Claude-only or fallback review; freeze them afterwards.
- [ ] Capture the declared baseline before editing sources.

## Test-first repair order

- [ ] Add failing Python and Node conformance fixtures for zero/one/multiple
  runtime sessions, explicit identity, explicit sole-session recovery, stale
  state, and malformed JSON; run them red.
- [ ] Implement the canonical JSON CLI and thin OpenCode adapter; run the same
  fixture matrix green.
- [ ] Add failing hook fixtures for success, exit failure, timeout, child
  cleanup, and warn/block/ignore; run them red.
- [ ] Normalize hook configuration and implement bounded process execution;
  run the hook fixtures green.
- [ ] Add a failing governance-artifact fixture with a fake 172-character
  bearer token and staged raw/cache paths; run it red.
- [ ] Add the local-only artifact policy, untrack the covered generated paths,
  and run the recurrence fixture green.

## Validation and commit gate

- [ ] Capture post-repair measurements using the exact commands in
  `wave_1/measurements.md`.
- [ ] Run all listed Python, Node, Git-index, parser, and no-regression checks.
- [ ] Run Claude-only review through `Start-CcgDualModelRun.ps1 -BackendMode
  claude`; if it has no usable output, obtain one read-only Codex fallback
  review.
- [ ] Commit only the frozen-contract allowlist with a Traditional Chinese body
  stating the issue set, baseline/result, validation, review evidence, and
  rollback boundary.
