# ChurchReport Trace findings remediation — design consistency analysis

Repository: SpeechMessageProducts
Active task: `.trellis/tasks/08-22-churchreport-trace-findings-remediation`

Review the already-approved `prd.md`, `design.md`, and `implement.md` plus the current source tree. Do not modify files. Analyze whether the implementation plan is internally consistent and identify concrete risks before implementation, especially:

1. F4 `DataverseTrace.BeginBackgroundOperation` and `AsyncLocal` context/statistics isolation, JSONL event schema, disposal and nesting/parallel behavior.
2. F2 `InMemoryDataContextSmallGroup` no-session fallback and cache retention/isolation.
3. F1 `SmallGroupDataList` deep snapshot, `Member` mutability, atomic publication, and all member-list call sites; determine the grep count and whether the >30 read-only fallback is needed.
4. Required Traditional Chinese documentation, UTF-8/CRLF/no-BOM constraints, tests, and scope boundaries.

Return a concise report with Critical/Warning/Info findings and specific file/line evidence. Treat the design documents as the intended contract; flag only real contradictions or implementation hazards.
