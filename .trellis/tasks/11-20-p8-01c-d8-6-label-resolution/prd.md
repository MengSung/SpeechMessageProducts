# P8-01c D8-6 標籤衝突修正與完成遷移驗收

## Goal

依使用者核定的 D8-6 補充，單欄同值重複才拒絕；跨欄同值異標籤以 new_fee 語系 1028 標籤命名並記錄 booking 差異。補上 Plan_SharedValueDifferentLabels_UsesFeeLabelAndRecordsDifference，完成 goodshepherdback migration plan/apply/verify/冪等性、指定 P8Live、off/verify=0/殘留0，以及回報、PROP-003、進度 DATA。

## Requirements

- Cross-field equality of an option value is valid. Duplicate option values within the same metadata field remain invalid.
- For shared values with differing labels, keep one `legacy-<value>` category and select the `new_fee.new_category` label in language 1028. Values present only in the booking field use that field's label.
- Include the differing booking label in the generated plan Markdown under a “標籤差異” section and register the behavior in P8-PROP-003.
- Do not infer semantic equivalence or create a second category for a shared value.
- Complete every P8-01c required step in 08A §5, including migration plan review, apply, migration verification, idempotent second apply, `OfferingCategories_Live_ListAndLookupResolve` under P8Live, and final `off`, verify=0, and zero residue.
- Preserve existing user changes in the working tree and never include secrets in source, command output, reports, or commits.

## Acceptance Criteria

- [ ] `Plan_SharedValueDifferentLabels_UsesFeeLabelAndRecordsDifference` exists and was observed failing before the implementation, then passes.
- [ ] Same-field duplicate metadata values fail closed; cross-field shared values resolve using the fee label, and booking-only values keep their booking label.
- [ ] Metadata fingerprints detect changes in either source field's labels.
- [ ] The plan JSON/Markdown and P8-PROP-003 retain the shared value identity and the distinct booking label.
- [ ] All P8-01c live requirements have safe, saved evidence; the organization ends off with verify=0 and no test residue.
- [ ] Report conclusion, Trellis state, progress DATA, commit, guard evidence, and remote SHA agree.

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
