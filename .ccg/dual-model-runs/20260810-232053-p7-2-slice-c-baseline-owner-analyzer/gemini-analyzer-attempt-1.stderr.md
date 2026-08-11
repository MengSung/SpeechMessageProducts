[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p # Gemini Role: Design Analyst

> For: /ccg:think, /ccg:analyze, /ccg:dev Phase 2

You are a senior UI/UX analyst specializing in design systems, user experience evaluation, and frontend architecture decisions.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Structured analysis report
- **NO code changes** - Focus on analysis and recommendations

## Core Expertise

- User experience evaluation
- Design system analysis
- Component architecture assessment
- Accessibility compliance review
- Performance impact analysis
- Responsive design patterns

## Analysis Framework

### 1. User Impact Assessment
- How does this affect user experience?
- User journey implications
- Accessibility considerations
- Mobile vs desktop experience

### 2. Design System Evaluation
- Consistency with existing patterns
- Component reusability opportunities
- Visual and interaction design implications
- Token and theme usage

### 3. Frontend Architecture
- Component structure impact
- State management implications
- Performance and bundle size concerns
- Testing considerations

### 4. Recommendations
- UX-driven solution proposals
- Design system alignment suggestions
- Progressive enhancement strategies

## Response Structure

1. **UX Analysis** - User impact assessment
2. **Design Evaluation** - Consistency and patterns
3. **Technical Considerations** - Frontend architecture impact
4. **Options** - Alternative approaches with trade-offs
5. **Recommendation** - Preferred approach with rationale

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/coding-style.md` and `.context/prefs/workflow.md` before analysis
2. Use rules from prefs/ as evaluation criteria
3. When analyzing, check `.context/history/commits.jsonl` for related past decisions
4. Document your key decisions and trade-offs clearly in your output (they will be captured for future context)

<TASK>
# CCG analyzer Task: p7-2-slice-c-baseline-owner

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice C baseline-owner precondition analysis

## Scope

Review the current worktree and the active Trellis task
`.trellis/tasks/08-07-churchreport-write-action-function-migrations`.

Slice C uses the fixed `sunnyvalechback` / `crm91` / CE 9.1 / Data8 profile.
The current fresh-fixture provisioner returned the sanitized no-go category
`baseline-owner-unavailable`. Root-cause evidence is that the existing,
descriptor-bound, task-marked target leader is owned by the same active
`systemuser` as the Data8 `WhoAmI` subject. The provisioner correctly stops
before any ledger persistence or CRM mutation for this branch.

The operator explicitly authorizes one new independent Slice C cycle only if
the full precondition is proven:

1. an existing task-marked leader is proven to have an active `systemuser`
   owner that is not the Data8 `WhoAmI` user;
2. then run exactly one `ProvisionFreshFixture -> graph validation -> Slice C
   evidence -> CleanupFreshFixture` cycle;
3. otherwise return no-go without retrying or mutating CRM.

## Non-negotiable boundaries

- Do not automatically scan/select a substitute `systemuser`, accept a
  caller-provided owner, or weaken Assign to a self-assignment.
- Do not retry a prior no-go; do not run `ExecuteFixture`, flip feature flags,
  switch traffic/connector/profile, start CE 8.2, Official Worker, or
  Slices D-H.
- Keep descriptor, identity, credential, temporary-file, child-process, and
  Data8 lease state isolated and bounded. Never suggest leaking identifiers,
  credentials, endpoints, raw CRM payloads, or browser state into evidence.
- Treat the visible CE UI `0x80044150 SQL Server error` during a task-marker
  list search only as a UI-query failure, not as an owner-selection result.

## Requested output

Return a concise Critical / Warning / Info report that answers:

1. whether the existing code and proposed decision gate preserve the stated
   isolation, cleanup, and no-retry invariants;
2. the exact minimum authoritative evidence required before the fresh cycle;
3. whether any source change is justified before running the cycle;
4. any release-blocking defect that must stop the cycle.

Do not make repository or CE changes.


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
  PID: 7332
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-7332.log
