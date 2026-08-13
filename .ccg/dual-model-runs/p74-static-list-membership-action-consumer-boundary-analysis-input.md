# P7.4 static-list membership action consumer boundary architecture review

Review this repository-local planning result only. The immutable capability matrix says
`list.members.add.many` and `list.members.remove.one` already have registry/Data8/ProductClient foundations,
but the ChurchReport consumer remains legacy.

Evidence from `ListManagementDataManager` shows that the calls coexist in the same user workflow with
ToolUtility Entity retrieve/update for contact primary list and attendance-related mutations. Replacing just the
member actions would create a Gateway-write plus ToolUtility-write composite without a unified transaction,
read-back/reconciliation, reverse-order cleanup, or single rollback owner.

Proposed decision: record a P7.4 local consumer-migration no-go; do not modify runtime/configuration/gates/CE;
retain the matrix temporary-legacy row; require a future independently planned whole-composite typed operation
family before retrying migration. Review for correctness, safety, session/profile isolation, resource lifecycle,
false completion, and missing prerequisites. Do not propose CE or deployment operations.

OUTPUT: Critical/Warning/Info findings and a PASS/FAIL recommendation.
