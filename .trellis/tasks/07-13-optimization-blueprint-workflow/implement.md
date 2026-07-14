# Optimization Blueprint Execution Plan

## Phase 1: Planning Artifacts

- [x] Produce the full issue and observation register.
- [x] Assign canonical issues to global W1-W5 without changing module ownership.
- [x] Define the local wave document contract and two-subagent sequence.
- [x] Complete local verification and independent Codex fallback review of the
      blueprint and workflow.
- [ ] Review the blueprint and workflow with the owner before activation.

## Phase 2: Review Infrastructure

- [ ] Add and verify a self-healing Claude-only review entrypoint that does not
      invoke Gemini.
- [ ] Define the main-session read-only Codex fallback review prompt and result
      format.

## Phase 3: Global W1 Activation

- [ ] For F01B, create `wave_1` documents covering the five selected issue IDs.
- [ ] Obtain Claude or fallback approval of the F01B wave documents.
- [ ] Repeat sequentially for F01C and F01D, with F01D-EXT-001 excluded.
- [ ] Run a separate zero-trust repair subagent for each approved local wave.
- [ ] Require local measurement, review approval, and a Traditional Chinese
      commit body before moving to the next workspace.

## Verification

- [ ] Inventory totals remain 322 records: 163 canonical, 13 runtime, and 146
      non-actionable observations.
- [ ] Canonical counts remain W1=35, W2=56, W3=32, W4=27, W5=13.
- [ ] No product file changes during Phase 1.
- [ ] No local wave exists without `plans.md`, `measurements.md`, and `goals.md`.
