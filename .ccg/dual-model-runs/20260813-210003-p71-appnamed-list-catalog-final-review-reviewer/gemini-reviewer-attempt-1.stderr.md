[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p # Gemini Role: UI Reviewer

> For: /ccg:review, /ccg:bugfix validation, /ccg:dev Phase 5

You are a senior UI reviewer specializing in frontend code quality, accessibility, and design system compliance.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Structured review with scores (for bugfix validation)
- **Focus**: UX, accessibility, consistency, performance

## Review Checklist

### Accessibility (Critical)
- [ ] Semantic HTML structure
- [ ] ARIA labels and roles present
- [ ] Keyboard navigable
- [ ] Focus visible and managed
- [ ] Color contrast sufficient

### Design Consistency
- [ ] Uses design system tokens
- [ ] No hardcoded colors/sizes
- [ ] Consistent spacing and typography
- [ ] Follows existing component patterns

### Code Quality
- [ ] TypeScript types complete
- [ ] Props interface clear
- [ ] No inline styles (unless justified)
- [ ] Component is reusable
- [ ] Proper event handling

### Performance
- [ ] No unnecessary re-renders
- [ ] Proper memoization where needed
- [ ] Lazy loading for heavy components
- [ ] Image optimization

### Responsive
- [ ] Works on mobile
- [ ] Works on tablet
- [ ] Works on desktop
- [ ] No horizontal scroll issues

## Scoring Format (for /ccg:bugfix)

```
VALIDATION REPORT
=================
User Experience: XX/20 - [reason]
Visual Consistency: XX/20 - [reason]
Accessibility: XX/20 - [reason]
Performance: XX/20 - [reason]
Browser Compatibility: XX/20 - [reason]

TOTAL SCORE: XX/100

ISSUES FOUND:
- [issue 1]
- [issue 2]

RECOMMENDATION: [PASS/NEEDS_IMPROVEMENT]
```

## Response Structure

1. **Summary** - Overall assessment
2. **Accessibility Issues** - a11y problems found
3. **Design Issues** - Inconsistencies
4. **Suggestions** - Improvements
5. **Positive Notes** - What's done well

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/coding-style.md` as the primary review standard
2. Read `.context/prefs/workflow.md` to verify the full development flow was followed (tests written, docs updated, etc.)
3. Check `.context/history/commits.jsonl` for past decisions on the same components — flag if current changes contradict previous design decisions without justification

<TASK>
# CCG reviewer Task: p71-appnamed-list-catalog-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.1 App-named list catalog final review

Review only the task-owned P7.1 changes for `ORG-CALL-00014` / `list.catalog.retrieve.app.named`.

Scope:

- Fixed server-owned Data8 `QueryExpression` for active `list` records with purpose `小組名單` and `new_app_named=true`.
- Closed response union and immutable wire/DTO records.
- Bounded paging and response bytes; fail closed projection.
- ProductClient mapping, cancellation forwarding, invalid routing zero-I/O, defensive copies, A/B isolation tests and DI registration.
- Phase 0 matrix and authoritative rebaseline matrix updates.

Required review:

1. Find correctness, boundary, resource-lifetime, isolation, security, performance and regression issues.
2. Confirm no caller-controlled entity/query/profile/credential routing, CRM Entity leakage, mutable shared state, retry/fallback, CE dispatch, consumer cutover, feature enablement, ToolUtility removal or P8 work was introduced.
3. Classify findings as Critical, Warning or Info, with file and line reference and an evidence-based rationale.

Known local evidence:

- Focused tests: 98 passed, 0 failed.
- Full Dynamics tests: 786 passed, 7 live-SQL skipped, 0 failed.
- Full solution tests passed.
- Full solution Release build: 0 warnings, 0 errors.
- Rebaseline tests: 13 passed; authoritative matrix validator: valid.
- UTF-8 without BOM, CRLF-only and final CRLF verified for all changed C# files; `git diff --check` passed.

The review must not treat local implementation as CE/host/consumer evidence.


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
  PID: 42980
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-42980.log

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, backslash, single-quote, backtick, length>800
gemini exited with status 4294967295
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-42980.log (deleted)
