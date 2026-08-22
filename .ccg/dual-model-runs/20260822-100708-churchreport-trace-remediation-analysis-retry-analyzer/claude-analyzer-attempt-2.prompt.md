ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: churchreport-trace-remediation-analysis-retry

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# ChurchReport Trace findings remediation — design consistency analysis

Repository: SpeechMessageProducts
Active task: `.trellis/tasks/08-22-churchreport-trace-findings-remediation`

Review the already-approved `prd.md`, `design.md`, and `implement.md` plus the current source tree. Do not modify files. Analyze whether the implementation plan is internally consistent and identify concrete risks before implementation, especially:

1. F4 `DataverseTrace.BeginBackgroundOperation` and `AsyncLocal` context/statistics isolation, JSONL event schema, disposal and nesting/parallel behavior.
2. F2 `InMemoryDataContextSmallGroup` no-session fallback and cache retention/isolation.
3. F1 `SmallGroupDataList` deep snapshot, `Member` mutability, atomic publication, and all member-list call sites; determine the grep count and whether the >30 read-only fallback is needed.
4. Required Traditional Chinese documentation, UTF-8/CRLF/no-BOM constraints, tests, and scope boundaries.

Return a concise report with Critical/Warning/Info findings and specific file/line evidence. Treat the design documents as the intended contract; flag only real contradictions or implementation hazards.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.