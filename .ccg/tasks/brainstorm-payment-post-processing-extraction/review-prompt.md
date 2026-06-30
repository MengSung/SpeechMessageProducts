ROLE_FILE: ~/.claude/.ccg/prompts/claude/reviewer.md
<TASK>
Review the current git diff for the ChurchReport payment post-processing workflow unification.

Check:
- provider-neutral payment core remains free of ChurchReport CRM and LINE dependencies
- TSPGController no longer duplicates CRM/LINE post-payment logic
- DonationFeePaymentProcessor accepts the common post-payment workflow and presenter without changing existing donation-specific behavior prematurely
- PaymentPostPaymentWorkflow stays provider-neutral
- tests cover MyPay, TSPG, Donation, and architecture boundaries

Output Critical/Warning/Info findings with file and line references when possible.
</TASK>
OUTPUT: Critical/Warning/Info review report.
