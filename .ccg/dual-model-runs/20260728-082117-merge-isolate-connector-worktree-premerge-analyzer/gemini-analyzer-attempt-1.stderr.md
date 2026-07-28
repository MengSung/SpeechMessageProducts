[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree -p # Gemini Role: Design Analyst

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
# CCG analyzer Task: merge-isolate-connector-worktree-premerge

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.2.IsolateConnector.Worktree

## Request
# Pre-Merge Analysis Request

Analyze the proposed local merge of branch `1.0.0.2.IsolateConnector.Worktree` into branch `1.0.0.2.IsolateConnector`.

## Repository State

- Source worktree: `D:/音訊科技產品/系統平台/SpeechMessageProducts/.worktrees/1.0.0.2.IsolateConnector.Worktree`
- Source tip: `c9dafdafa34541ae57753bfcc8db4c7338853cff`
- Target worktree: `D:/音訊科技產品/系統平台/SpeechMessageProducts`
- Target tip: `82df2440e17708172ee4706c5f54d2932e569e7a`
- Merge base: `18ef7b85a9b5055621fe8f731436d4f59679f293`
- Both worktrees were clean before creating this merge-task metadata.
- Target has one unique task-archive commit; source has seven unique commits.
- Proposed source diff: 406 files changed, 40,831 insertions, 407 deletions.
- No remote push is requested.

## Source Commit Set

1. `72cbf0e7c` docs: define global safety guardrails
2. `58657c0f9` Dynamics 365 no-SDK Gateway Phase 0 planning and architecture
3. `f90ef06c3` ChurchReport Package 1 controlled queries and capacity protection
4. `41f7e1eaa` Dynamics 365 IFD/ADFS OAuth layered enablement and diagnostics
5. `9978261c2` ADFS authorization-code/refresh-token support and local diagnostics
6. `0385e9aeb` Dynamics 365 9.1 IFD token-failure diagnosis and report
7. `c9dafdafa` D365 password-security hardening and review records

## Sensitive Areas

- OAuth/ADFS token acquisition and refresh
- Secret resolution and password handling
- HTTP transports and Dynamics Web API access
- Organization capacity/admission controls
- ChurchReport integration and configuration
- New projects, tests, diagnostic scripts, generated review artifacts, and task records

## Required Output

Provide a pre-merge readiness analysis with these sections:

1. `Critical` — conditions that must block the merge.
2. `Warning` — risks that should be verified or mitigated before/after merge.
3. `Info` — observations and suggested verification commands.
4. `Merge Strategy` — safe local merge sequence, conflict hotspots, and rollback points.
5. `Test Matrix` — concrete build/test/static checks appropriate for this repository and change set.

Do not modify repository files. Verify claims against the actual branch diff and repository configuration. Distinguish committed generated evidence from product code, and do not treat provider credentials as available unless explicitly configured.


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
  PID: 16476
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-16476.log
