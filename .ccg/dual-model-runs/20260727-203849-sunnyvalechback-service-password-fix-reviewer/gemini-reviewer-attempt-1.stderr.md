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
# CCG reviewer Task: sunnyvalechback-service-password-fix

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Sunnyvalechback 9.1 service credential fix review

## Scope

Review a one-line high-risk configuration change for Dynamics 365 CE 9.1
on-premises IFD login repair. Do not ask for or print the real password.

## User goal

ChurchReport frontend member login uses account `zz` with password `zz`.
That account is a frontend member credential, not an ADFS/D365 credential.
Before the member credential can be verified, ChurchReport opens a backend
Dynamics 365 Organization.svc connection using the configured service account.

## Evidence gathered

- `sunnyvalechback.speechmessage.com.tw` redirects to `adfsdev91`.
- Directly posting `zz/zz` to ADFS fails, which is expected because `zz/zz`
  is not an ADFS account.
- LDAP bind to the DC succeeds with the supplied administrator account.
- The active ChurchReport `CrmConnection` uses:
  - Organization: `sunnyvalechback`
  - ServerUrl: `https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc`
  - Username: `SPEECHMESSAGE\Administrator`
  - Password: previously a stale 6-character value
- The user supplied the current D365/DC administrator password separately.
- After updating the active `CrmConnection:Password` to the supplied current
  administrator password, an end-to-end local Kestrel test on
  `http://localhost:43371/Authentication/ProcessLogin` with frontend
  `zz/zz` returned success:
  `DisplayViewType=IntegrateView`, `message=login success`, and a full name.

## Redacted diff

```diff
 SpeechMessageProducts.ChurchReport/appsettings.json
-    "Password": "<old stale 6-character service password>", // CRM password
+    "Password": "<current supplied D365 9.1 service password>", // CRM password
```

## Review questions

1. Is the diagnosis logically supported by the evidence?
2. Is this minimal config change an acceptable repair for the immediate
   frontend login failure?
3. What residual risks should be called out, especially plaintext secrets,
   service account privilege, scratch artifacts, and future no-SDK/OAuth work?
4. Are there any Critical findings that should block reporting the login fixed?

## Expected output

Return Critical / Warning / Info findings. Do not include or request any real
password value.


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
  PID: 30456
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-30456.log
