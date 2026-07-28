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
# CCG reviewer Task: phase0-no-sdk-removal-gate

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Review request: Phase 0 no-SDK removal gate

Role: reviewer

Please review the Phase 0 Dynamics 365 no-SDK removal gate updates.

User requirement:

- Final solution must not reference any Microsoft CRM/Dataverse SDK DLL/package/type.
- PowerPlatform.Dataverse.Client is a temporary legacy dependency only.
- PowerPlatform.Dataverse.Client must ultimately be removed from SpeechMessageProducts.sln.
- All ProjectReference/package/DLL references to PowerPlatform.Dataverse.Client and Microsoft CRM SDK assemblies must be removed after consumers migrate.
- Phase 0 must not break the current build by deleting the legacy project prematurely; it should record inventory/gates only.

Files to inspect:

- .trellis/tasks/07-23-dynamics-connection-compatibility/prd.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/design.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/implement.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-inventory.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.schema.json
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-runtime-capacity-adr.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-verification.md
- docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md
- .ccg/tasks/dynamics-connection-compatibility/task.json

Known current SDK graph that must be represented as final-removal findings:

- SpeechMessageProducts.sln includes PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj.
- ToolUtility/ToolUtility.csproj ProjectReference includes ../PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj.
- ToolUtility.Tests/ToolUtility.Tests.csproj references Microsoft.CrmSdk.CoreAssemblies 9.0.2.56.
- PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj references Microsoft.PowerPlatform.Dataverse.Client 1.1.32 and Microsoft.Xrm.Sdk source types.
- SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj references Microsoft.PowerPlatform.Dataverse.Client 1.2.10.
- SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj references Microsoft.Crm.Sdk.Proxy.dll through a Dynamics 365 SDK DLL HintPath.

Verification already run locally:

- JSON validity: schema and matrix parse with ConvertFrom-Json.
- Mandatory new SpeechMessage.Dynamics.sln stale wording scan found no rejected wording.
- Final no-SDK removal wording scan found expected references.
- git diff --check on focused artifacts passed.

Please check:

1. Does the Phase 0 inventory/gate fully represent the current SDK reference graph?
2. Is the final no-SDK end state unambiguous that PowerPlatform.Dataverse.Client must be removed/deleted or moved out of buildable source, not wrapped or retained?
3. Does Phase 0 avoid prematurely deleting references before the replacement no-SDK path exists?
4. Are there any contradictions with the existing-solution topology, i.e., add new Dynamics projects to SpeechMessageProducts.sln rather than require a new SpeechMessage.Dynamics.sln?
5. Are there missing Critical/Warning findings around session leakage, memory leakage, connection pooling, or SDK-removal enforcement?

Output: Critical / Warning / Info findings with file references and concise remediation.



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
  PID: 28792
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-28792.log
