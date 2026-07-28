ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: dynamics-connection-progress-audit

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics 365 connection split progress audit

Perform a read-only, evidence-based audit of the current Dynamics 365 connection
split / no-SDK access gateway work. Do not modify files.

## Repository state

- Current branch: `1.0.0.3.Gateway&Embedded.Worktree`
- Current HEAD is based on the merged isolate-connector work (`f9e544e0`) plus
  CCG archival commits; product implementation is already committed.
- The user wants a status report, not code changes.

## Primary artifacts to inspect

- `.trellis/tasks/07-23-dynamics-connection-compatibility/task.json`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/assessment.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase*-verification.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase3-*.md`
- `.ccg/tasks/dynamics-connection-compatibility/task.json`
- `.ccg/tasks/dynamics-connection-compatibility/review.md`
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`
- `eng/no-sdk-source-roots.json`
- `SpeechMessage.Dynamics.*`
- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `.ccg/tasks/archive/2026-07/merge-isolate-connector-worktree/verification.md`
- `.ccg/tasks/archive/2026-07/merge-isolate-connector-worktree/review.md`

## Questions

1. Distinguish the Trellis workflow phase, the task-specific implementation
   Phase 0-6, and the architecture rollout stage. State the real current phase
   based on code and verification evidence, not just metadata.
2. Evaluate SPEC/PRD/design/implement-plan completeness and quality.
3. Evaluate actual implementation progress by Phase 0-6 and identify what is
   complete, partial, blocked, or not started.
4. Identify stale or contradictory status documents and explain their impact.
5. Identify verified technical/release blockers, especially ADFS/IFD live
   validation, durable multi-host coordination, Phase 4 soak/fault/performance
   gates, feature-flag state, and final SDK removal.
6. Reconcile with the latest merge verification: 47 Dynamics unit tests pass,
   4 smoke tests pass with live CRM disabled, focused builds pass; full solution
   retains 23 baseline failures and ToolUtility.Tests restore mismatch.
7. Provide a concise overall grade and prioritized next steps. Do not expose
   credentials or speculate beyond repository evidence.

## Output

Write a Traditional Chinese report with:

- Executive conclusion
- Phase table
- Spec assessment
- Implementation assessment
- Documentation/traceability issues
- Blockers and risks (Critical/Warning/Info)
- Prioritized next steps
- Overall evaluation


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