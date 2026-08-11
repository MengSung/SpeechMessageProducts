ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: p72-weekly-report-precondition-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 weekly-report precondition analysis

## Question

Assess whether the P7.2 Slice C FreshPreflightProbe requires exactly one active
weekly report across the entire organization for a UTC Sunday, or exactly one
active weekly report only for the descriptor-bound transfer target list and
that UTC Sunday. Identify whether the current `not-exactly-one-active` result
can distinguish zero matches from duplicate matches.

## Evidence

The implementation constructs this bounded query:

```csharp
var query = new QueryExpression("new_group_present_weekly_report")
{
    ColumnSet = new ColumnSet("new_group_present_weekly_reportid"),
    NoLock = true,
    TopCount = 2
};
query.Criteria.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, targetListId);
query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
query.Criteria.AddCondition("new_sunday_date", ConditionOperator.Equal, weekStartUtc.UtcDateTime);
var rows = _service.RetrieveMultiple(query);
if (rows is null || rows.MoreRecords || rows.Entities.Count != 1)
{
    return false;
}
```

The sanitized live result was only `weeklyReport=not-exactly-one-active`; it
does not contain IDs, names, dates, endpoint, credentials, raw response, or
raw exceptions.

The user reports that each group normally has a weekly report for the same
Sunday, so multiple reports exist across different groups.

## Constraints

- No CE call, mutation, descriptor read, or data exposure is authorized for
  this analysis.
- Do not suggest modifying CRM data until the per-target-list cardinality is
  diagnosed by an appropriate bounded read-only method.
- Keep conclusions concise and separate observed facts from hypotheses.

## Output

Return: (1) interpretation of the query, (2) what is and is not proven by the
sanitized outcome, (3) safe next diagnostic question, and (4) any defect in
the implementation or user-facing wording.


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
