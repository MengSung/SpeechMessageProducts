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
# CCG reviewer Task: dynamics-local-gateway-decision

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Review request: D365 Local Gateway vs Embedded decision visualization

Review the following design-only deliverable for technical correctness,
clarity, consistency, and misleading claims. This task does not authorize any
product-code changes.

## Deliverable

`C:/Users/Administrator/.codex/visualizations/2026/07/29/019fab98-842e-78a1-9b65-ee684c875612/dynamics-local-gateway-decision.html`

## Architecture context

- The user's primary development goal is to start ChurchReport and a localhost
  Gateway together in Visual Studio 2026 and observe/debug both.
- Production should use a centralized Gateway for five to ten products.
- Products should keep one `Gateway` execution contract; local versus central
  is selected by `Gateway.Endpoint`, not a separate `LocalGateway` enum.
- Local Gateway is a separate process and owns its own process-local SDK client,
  connection pool, credentials, health, and disposal lifecycle.
- Embedded runs inside the product process and therefore couples SDK/runtime,
  credentials, pool, health, and cleanup to every product host.
- Embedded should be deferred from the initial supported release but not
  deleted before the user approves the revised design.
- No ChurchReport or product implementation is changed in this task.

## Review criteria

Report Critical, Warning, and Info findings. Verify especially:

1. The distinction among Central Gateway, Local Gateway, and Embedded is correct.
2. The Visual Studio multiple-startup workflow is realistic.
3. The JSON examples make clear that Local and Central both use
   `ExecutionMode: Gateway` and differ only by endpoint.
4. The recommendation does not overstate that Embedded must be removed.
5. The diagram does not imply one physical connection pool can be shared across
   processes; each Gateway process owns its process-local pool while central
   coordination governs aggregate capacity.
6. The HTML fragment is accessible, theme-aware, responsive, and its scenario
   buttons work without undefined identifiers.

Return a concise review suitable for saving in the CCG task's `review.md`.


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
  PID: 5140
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-5140.log
