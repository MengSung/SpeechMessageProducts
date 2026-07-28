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
# CCG analyzer Task: dynamics-gate0-gate1-execution

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics 365 Gate 0 / Gate 1 execution analysis

Analyze the next execution step for the existing high-risk task
`.trellis/tasks/07-23-dynamics-connection-compatibility`. This is analysis only;
do not modify repository files or remote systems.

## Authorized scope

- The owner authorizes product code changes, VM configuration, and WinRM probes.
- Targets are the lab D365/ADFS environment at `192.168.50.10` and
  `192.168.50.20`.
- WinRM over TCP 5985 and WSMan 3.0 currently answer on both targets.
- Product traffic and `Package01FeeReadsEnabled` must remain disabled until all
  live gates pass.
- Existing CRM relying-party trusts must not be replaced or destructively
  modified. Prefer additive, uniquely named lab objects with documented rollback.
- Secrets and token values must not be printed, committed, or retained in logs.

## Objective

Review a safe, evidence-first procedure for:

1. Identifying exact VM roles and obtaining a recoverable pre-change baseline.
2. Read-only ADFS inventory: clients, application groups, relying-party trusts,
   properties, CRM resource/audience, and supported OAuth endpoints/grants.
3. Determining whether this exact CE 9.1 IFD target can issue a service-identity
   token usable for CRM Web API through `client_credentials`.
4. Testing `token -> WhoAmI -> service document -> $metadata` without enabling
   product traffic.
5. Stopping and returning to architecture selection if the target cannot support
   the required non-user, non-refresh-token-persistence flow.
6. Only if the live experiment succeeds, implementing `client_credentials` in
   `AdfsOAuthTokenProvider` with TDD, bounded token state, single-flight refresh,
   deterministic disposal, redaction, and no per-user/session storage.
7. Proving no session leakage or memory/socket/timer/cache growth and preserving
   maximum safe throughput with bounded admission rather than unbounded pooling.

## Known repository facts

- Current provider implements refresh-token and local-dev password grants, not
  client credentials.
- The existing provisional client ID is not proven registered on the target.
- The only runtime host coordinator is non-durable/in-memory.
- The Gateway still accepts workload subject from the request body and is not
  production-ready.
- Unit tests previously passed 47/47; smoke tests passed 4/4 with live CRM off.
- Full Phase 4 soak/fault/leak/performance tests do not yet exist.

## Review questions

Return a Traditional Chinese report with Critical/Warning/Info findings and a
PASS/FAIL verdict for proceeding with Gate 0/1. Include exact stop conditions,
minimal safe ADFS changes, rollback evidence, and the tests/metrics required to
support the no-session-leak, no-memory-leak, high-throughput requirements.


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
  PID: 30040
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-30040.log
