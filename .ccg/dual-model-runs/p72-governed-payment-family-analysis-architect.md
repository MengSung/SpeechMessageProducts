# CCG architect Task: p72-governed-payment-family-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 governed recurring payment-return write family — architecture analysis

Analyze the following proposed new P7.2 child in this repository. This is a high-risk CRM test-write family; do not recommend bypassing safety gates.

Confirmed baseline:
- Historical P7.2 Slice C is permanently closed: write-not-committed no-go with cleanup completed. Its nonce, ledger, descriptor, fixture and evidence cannot be reused or replayed.
- Existing P72DonationPaymentLocalDecision/PlanBuilder are pure local-only and keep CE dispatch/consumer disabled.
- Legacy RecurringDonationPaymentProcessor mixes dedup read, contact card update, fee create, fee-owner assign, booking update, and notification with no proven transaction/read-back/reconcile/cleanup boundary.
- Test CE mutations are permitted only for a fresh task-owned fixture with a new nonce, new ledger, fixed allowlist, read-only preflight=go, exactly one dispatch, exact read-back/reconcile and deterministic cleanup. Never scan or guess CRM owner/users. Feature gates/traffic/P7.5/P8 remain unchanged.

Proposed first local slice: a pure P72GovernedPaymentCycleAdmission contract for payments.fee.update.after.payment. It must not touch Data8/CRM SDK/network/files/feature flags. It accepts only immutable de-identified stage evidence and returns bounded fail-closed dispositions. Fee create/owner assign/booking completion/notifications remain separate future writer slices.

Review the repository patterns and answer in Traditional Chinese:
1. Critical design risks or missing invariants in the new admission contract.
2. The minimum descriptor/ledger/preflight/allowlist/read-back/cleanup rules a future governed CE executor must enforce.
3. Whether any existing Slice C fixture infra can be reused (explain safe answer).
4. Test cases that must be RED/GREEN before implementation.
5. Whether the intended scope wrongly implies P7.4/P7.5/P8/traffic or legacy consumer changes.

Output: Critical / Warning / Info, concise and evidence-driven. Do not include secrets or external identifiers.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.