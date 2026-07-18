# B01-SEC-003 Wave 2 Execution Plan

## 1. Verify External Preconditions

- Locate and inspect the three redacted evidence packages.
- Cross-check deployed-caller confirmation against the existing 17/17 source
  inventory without treating source absence as external-binary proof.
- Stop before product/test edits if CRM proof, caller confirmation, or probe
  readiness is missing or discloses prohibited data.

## 2. Load Development Contracts

- Read the Trellis backend and shared specifications.
- Read the complete B01 Wave 2 plans, measurements, and goals.
- Inspect every allowlisted source/test file and the current direct-comparison
  baseline before designing tests.

## 3. Test-first Repair

- Add failing strict-envelope, migration/concurrency, compatibility-key,
  route/claims/response, persistence, and ToolUtility delegation tests.
- Implement the minimum approved verifier, CRM store, controller integration,
  compatibility key, central ContactService behavior, and DI registration.
- Keep every non-allowlisted product/test path unchanged.

## 4. Runtime And Local Validation

- Run all required focused tests and direct-comparison/sensitive-sink searches.
- Deploy the candidate to the approved non-production target and capture the
  redacted success/failure `ProcessLogin -> SetupSystemData` route proof.
- Confirm relevant B02/B03/B04B/B06A/B06B paths receive `KEY`, not `RAW`.
- Verify exact allowlist, UTF-8 without BOM, CRLF, and `git diff --check`.

## 5. Review, Commit, And Close

- Run Claude-only review and resolve every Critical/Warning finding.
- Record any unavailable/no-output review state without claiming approval.
- Commit with a Traditional Chinese subject/body, push, update the global Wave
  2 tracker, archive this CCG task, and leave B02 queued.
