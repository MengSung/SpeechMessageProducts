ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p74-memberinfo-basic-info-boundary-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG architecture review: P7.4 ORG-CALL-00030 no-go boundary

Repository: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree`

Task: `.trellis/tasks/08-14-p74-memberinfo-basic-info-consumer-boundary`

Review only the current task's repository-only feasibility assessment. Do not modify runtime, configuration, feature gates, CE, fixtures, CRM data, P7.5 or P8.

Facts to verify:

- `MemberInfoController.UpdateContactInfo` can update four contact fields: `mobilephone`, `address2_line1`, `customertypecode`, `new_spiriitual_identity`.
- Existing typed/Data8 capability `memberinfo.contact.update.basic.info` accepts only `contactId`, `phone` and `address` and reads back only the two string fields; OptionSet fields fail closed.
- Partial consumer wiring would create a Gateway + ToolUtility split-brain composite or silently change four-field behavior.
- Historical P7.2 Slice C is permanently closed after `write-not-committed` no-go and exact cleanup; no retry/reuse.
- All feature gates remain false.

Check whether the task artifacts correctly record:

1. no-go and its causal source evidence;
2. fail-closed prohibition on partial migration, dual-write, SDK bridge, fallback and mutation retry;
3. precise recovery conditions for a future four-field DTO-only write family;
4. no accidental CE/traffic/P7.5/P8 authorization;
5. scope and encoding/documentation consistency.

Output Critical/Warning/Info with exact file paths. If external review cannot finish, clearly state the provider/session/quota reason and do not invent findings.


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
