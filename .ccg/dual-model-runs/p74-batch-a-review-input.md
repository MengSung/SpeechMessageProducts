# P7.4 Batch A review: atomic Package01 fee projection

Review the current uncommitted diff for P7.4 Batch A.

The production change is limited to `DonationFeeQueryService`:

- typed Package01 DTO mapping and amount calculation now complete in request-local locals;
- the existing `DonationPaymentFormModel` is changed only after the entire mapping succeeds;
- a regression test reproduces a malformed typed DTO and proves no partial model mutation.

Verify correctness, null/fault behavior, overflow behavior, async/cancellation semantics,
cross-request isolation, resource ownership, documentation, and scope. Check that no feature
gate enablement, CE request, traffic switch, ToolUtility removal, P7.5, or P8 work was added.

Output Critical / Warning / Info findings with exact paths and line numbers. Treat only actual
code evidence as a finding; do not demand CE activation or SDK Entity compatibility for this
disabled local-only read batch.
