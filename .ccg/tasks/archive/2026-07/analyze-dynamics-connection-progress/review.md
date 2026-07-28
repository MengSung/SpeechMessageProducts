# Dynamics 365 connection split progress audit

## Verdict

The architecture specification is strong, but implementation is non-linear and
not production-ready. The real state is Trellis Execute / CCG implementation,
with Phase 1 substantially complete, Phase 2 and Phase 3 partially implemented,
Phase 4's planned soak/fault/performance suite not implemented beyond the
current focused unit and env-gated smoke tests, consumer wiring staged behind a disabled feature
flag, and Phase 6 SDK removal not started.

## Verified evidence

- `SpeechMessage.Dynamics.Tests`: 47 passed after merge.
- `SpeechMessage.Dynamics.SmokeTests`: 4 passed with live CRM disabled.
- Focused ChurchReport, Gateway, and ProductClient Release builds passed.
- `Package01FeeReadsEnabled` remains `false`.
- The configured CE 9.1 ADFS ClientId is still documented as unverified.
- Only the non-durable in-memory runtime-host coordinator is implemented.
- Gateway scaffolding accepts workload identity from the request body and has no
  production workload-authentication middleware, so it must not be enabled as a
  production shared Gateway.
- The no-SDK scanner remains report-only and omits
  `SpeechMessage.Dynamics.ProductClient` from its source-root manifest.
- Legacy SDK/package/project references are still present.

## Traceability findings

- `implement.md` and the CCG review still say implementation has not started.
- PRD acceptance criteria remain entirely unchecked.
- Trellis and CCG task JSON files contain UTF-8 BOM; Trellis silently omits the
  task from `task.py list/current` when it parses with plain UTF-8.
- `implement.jsonl` and `check.jsonl` still contain placeholder examples.
- Task notes and next actions lag the merged code and current CE 9.1 target.

## External analysis

The project self-healing dual-model entrypoint completed successfully with both
Gemini and Claude, without quota fallback:

- `20260728-110803-dynamics-connection-progress-audit-analyzer`

Both models agreed on the main conclusions: strong specification, partial
implementation, stale tracking documents, and blocking work around ADFS live
validation, durable coordination, Phase 4 verification, and final SDK removal.

The final report review also completed successfully with both backends and a
PASS verdict, without quota fallback:

- `20260728-111937-dynamics-connection-progress-report-review-reviewer`

## Risk reconciliation

- ADFS/IFD live validation and production Gateway authentication are task-level
  blockers.
- Durable multi-host coordination and Phase 4 soak/fault/performance evidence
  are release blockers, not blockers for continued local development.
- The 23 repository-wide test failures and ToolUtility.Tests framework mismatch
  are pre-existing baseline debt; they are warnings for release confidence, not
  regressions caused by this task.
- Based on the owner's prior explicit conversation instruction, historical
  credential exposure is treated as temporary development material scheduled
  for later rotation; it is not treated as the current sequencing blocker in
  this report. No credential value is reproduced here.
