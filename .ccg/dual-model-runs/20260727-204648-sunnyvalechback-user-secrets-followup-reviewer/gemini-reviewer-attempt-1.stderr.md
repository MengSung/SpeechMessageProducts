[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree -p # Gemini Role: UI Reviewer

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
# CCG reviewer Task: sunnyvalechback-user-secrets-followup

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Sunnyvalechback 9.1 User Secrets follow-up review

## Scope

Review the follow-up fix after a prior security review objected to storing the
current Dynamics 365 service password directly in tracked `appsettings.json`.
Do not request or print the real password.

## Final implementation

Tracked files:

```diff
 SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj
+    <UserSecretsId>speechmessageproducts-churchreport-local-dynamics</UserSecretsId>

 SpeechMessageProducts.ChurchReport/appsettings.json
-    "Password": "<old stale service password>", // CRM password
+    "Password": "REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT", // CRM password: local Development uses User Secrets; production uses environment/secret manager override
```

Local machine state:

- User Secrets contains key `CrmConnection:Password`.
- The value is not printed in logs or this prompt.
- `ASPNETCORE_ENVIRONMENT=Development` was used for local Kestrel testing.

Verification:

- Started ChurchReport locally on `http://localhost:43371`.
- GET `/Authentication/Login` returned HTTP 200.
- POST `/Authentication/ProcessLogin` with frontend member account `zz` and
  password `zz` returned HTTP 200 JSON success:
  - `DisplayViewType=IntegrateView`
  - `message=login success`
  - full name returned

Review questions:

1. Does the follow-up remove the Critical plaintext-secret finding for tracked
   source files?
2. Is the local Development login verification still strong evidence that the
   original login failure is fixed?
3. What remaining non-blocking warnings should be reported, especially
   production secret provisioning and scratch artifacts?

Expected output:

Return Critical / Warning / Info findings. State clearly whether any Critical
finding still blocks reporting the login fixed.


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
  PID: 3952
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-3952.log
