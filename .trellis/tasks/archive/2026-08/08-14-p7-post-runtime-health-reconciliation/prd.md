# P7 post-runtime-health current matrix reconciliation

## Goal

Refresh the P7 authoritative gap snapshot after the archived
`ORG-CALL-00003/runtime.health.whoami` ProductClient child changed repository
implementation evidence. The refresh must be a task-owned, deterministic,
offline artifact; it must not modify archived evidence or perform CE, network,
credential, feature-gate, traffic, consumer, or deployment operations.

## Confirmed facts

- The archived 70-row matrix remains immutable historical evidence and still
  reports ORG-CALL-00003 as `productClient=not-implemented`.
- Commit `036032f5` added the local-only typed ProductClient and DI registration;
  the child was checked, committed, and archived, with no consumer or CE action.
- The existing offline `build_rebaseline.py` is the only allowed matrix source;
  it reads fixed repository artifacts and source and emits de-identified finite
  states. Direct invocation is required because the archived wrapper's relative
  analyzer path is no longer valid after archival.

## Requirements and constraints

1. Run the fixed analyzer with output inside this task directory only, then
   validate the generated JSON with the same analyzer.
2. Preserve the canonical 70 call-site IDs, Phase-0 hash, operation IDs,
   historical Slice C `no-go-closed`, and all independent evidence dimensions.
3. Prove from current source scanning that ORG-CALL-00003 now has
   `productClient=implemented` while `consumer=not-migrated`, CE/host evidence
   remain unchanged, and it is not silently promoted to rollout or P7.5-ready.
4. Produce a bounded matrix summary and a short reconciliation report with no
   CRM IDs, names, endpoints, credentials, tokens, raw exceptions, or secrets.
5. Use the refreshed snapshot to select the next independently verifiable P7
   capability; do not invent an Owner, replay Slice C, or create P8/P7.5 work.

## Acceptance criteria

- [ ] Task-owned matrix JSON is generated and validates with zero errors.
- [ ] Exactly 70 rows and the canonical Phase-0 hash are preserved.
- [ ] ORG-CALL-00003 reports ProductClient implemented and remains
      consumer-not-migrated, CE/host evidence-pending, and temporary-legacy.
- [ ] Historical Slice C remains no-go-closed and no external operation occurs.
- [ ] Output is deterministic, UTF-8 without BOM, CRLF-only with final CRLF,
      de-identified, and `git diff --check` is clean.
- [ ] A next-child recommendation is based only on the refreshed matrix and
      current source evidence; P7.5/P8 remain fail-closed.
