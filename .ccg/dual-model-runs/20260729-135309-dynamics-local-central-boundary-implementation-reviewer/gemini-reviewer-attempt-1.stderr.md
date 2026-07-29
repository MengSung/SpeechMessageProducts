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
# CCG reviewer Task: dynamics-local-central-boundary-implementation

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics Local/Central Gateway boundary implementation review

Review the current implementation milestone against:

- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `docs/superpowers/plans/2026-07-29-dynamics-local-central-gateway-boundary.md`

Inspect these changed or newly created files directly (two are untracked and therefore do not appear in a normal `git diff`):

- `SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs`
- `SpeechMessage.Dynamics.ProductClient/Configuration/GatewayProductClientLimits.cs`
- `SpeechMessage.Dynamics.ProductClient/Configuration/GatewayProductDynamicsOptionsValidator.cs`
- `SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs`
- `SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs`
- `SpeechMessage.Dynamics.Tests/ProductModeOptionsTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs`
- `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`

Focus on Critical/Warning findings only:

1. Central and Local must remain the same `ExecutionMode=Gateway` contract selected by endpoint.
2. Startup validation must fail closed for HTTP, URI user-info/query/fragment, raw CRM Web API/SOAP endpoints, invalid API prefix, unsafe/oversized profile alias, inactive Embedded branch, and response bounds outside 1 KiB through 8 MiB.
3. A request cannot override the deployment-configured ProfileAlias and no HTTP request is sent on mismatch.
4. `HttpClient.SendAsync` must use `ResponseHeadersRead`; Content-Length and chunked bodies must share one hard byte limit.
5. Every response, content stream, rented array, temporary payload buffer, request, and cancellation path must have deterministic bounded ownership and cleanup. Check for memory, stream, socket, cancellation, or session leakage.
6. Caller cancellation must remain an `OperationCanceledException`; other transport/read failures must be sanitized and must not log raw URLs, bodies, credentials, tokens, sessions, or exception objects.
7. The bounded reader must have no off-by-one, overflow, unbounded allocation, infinite-loop, use-after-clear, or invalid disposal behavior.
8. `System.Security.Cryptography.Xml` changes only from vulnerable `10.0.9` to patched `10.0.10`; do not recommend keeping Data8 permanently.
9. Tests must actually fail on the old implementation and prove the relevant behavior rather than merely restating implementation details.

Local evidence already obtained:

- Product options RED: 12 failures / 4 passes before validator.
- Expanded ProductModeOptionsTests GREEN: 26/26.
- GatewayProductClientTests RED: profile override, declared overage, chunked overage, and caller cancellation failed before implementation.
- GatewayProductClientTests GREEN: 7/7.
- Complete Dynamics suite after implementation: 125/125.
- NuGet vulnerable baseline: five High advisories for `System.Security.Cryptography.Xml 10.0.9`.
- Post-upgrade audit: no vulnerable package reported for the Data8 project.
- Solution Release build before final refactor: 0 warnings, 0 errors; full build will be rerun after review fixes.

Output a concise report grouped by Critical / Warning / Info. Cite exact files and lines. Do not modify files.


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
  PID: 16408
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-16408.log
