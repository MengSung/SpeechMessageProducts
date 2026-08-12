# P7.2 dedication payment-return write-boundary analysis

Inspect only the current repository state and provide a concise architecture/safety review for a new P7.2 child.

## Objective

Design and locally verify a fail-closed, DTO-only replacement boundary for the recurring dedication-payment return chain. The current legacy chain starts at `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs:HandlePaymentReturn`, retrieves a dedication booking, checks fee period `001`, may update a contact, creates a fee, then updates the dedication booking. It currently uses ToolUtility and SDK Entity objects.

## Strict constraints

- Do not propose CE requests, mutations, feature enablement, traffic cutover, fallback, dual-write, retry, or reuse of any historical Slice C cycle/nonce/ledger/fixture/descriptor.
- The historical Slice C CE cycle is closed after `write-not-committed` no-go and exact cleanup.
- P7.2 D-H has only local-only contracts. A future CE family requires a new child, nonce, ledger, task-owned fresh fixture, preflight, one dispatch, exact read-back/reconciliation, and deterministic cleanup.
- No caller-controlled profile, endpoint, credential, organization, owner, CRM entity, or raw payment data may become authority.
- Preserve A/B user/profile isolation and deterministic resource release; timeout/ambiguous/partial/cleanup uncertainty must fail closed with no replay.
- P7.5/P8 are out of scope.

## Existing local-only baseline

- `SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalDecision.cs`
- `SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalPlanBuilder.cs`
- `SpeechMessage.Dynamics.Tests/P72DonationPaymentLocalDecisionTests.cs`

## Required output

1. Exact call-chain and mutation families that must remain separately governed.
2. Smallest safe next local implementation increment; include concrete types/files if inferable.
3. No-go conditions which should prevent consumer cutover or CE evidence.
4. Test requirements for idempotency, timeout-after-dispatch, read-back, cleanup, A/B isolation, and lifecycle.
5. Critical / Warning / Info findings only; do not write code.
