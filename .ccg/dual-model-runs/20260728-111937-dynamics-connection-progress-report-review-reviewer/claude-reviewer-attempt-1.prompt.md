ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-connection-progress-report-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Review the Dynamics 365 connection split progress audit

Perform a read-only factual review. Do not modify files.

Review these audit artifacts against the repository evidence:

- `.ccg/tasks/analyze-dynamics-connection-progress/review.md`
- `.ccg/tasks/analyze-dynamics-connection-progress/task.json`
- `.ccg/dual-model-runs/20260728-110803-dynamics-connection-progress-audit-analyzer/claude-analyzer-attempt-1.stdout.md`
- `.ccg/dual-model-runs/20260728-110803-dynamics-connection-progress-audit-analyzer/gemini-analyzer-attempt-1.stdout.md`

Primary source evidence remains:

- `.trellis/tasks/07-23-dynamics-connection-compatibility/`
- `.ccg/tasks/dynamics-connection-compatibility/`
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`
- `SpeechMessage.Dynamics.*`
- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `eng/no-sdk-source-roots.json`
- `.ccg/tasks/archive/2026-07/merge-isolate-connector-worktree/{review.md,verification.md}`

Check specifically:

1. Phase classification is evidence-based and distinguishes highest phase
   touched from highest phase completed.
2. ADFS/IFD, Gateway workload authentication, durable coordination, Phase 4
   verification, feature-flag state, and SDK removal are correctly classified.
3. The 23 full-suite failures and ToolUtility.Tests mismatch are clearly marked
   as pre-existing baseline debt, not task regressions.
4. Historical credentials are not printed and are treated as owner-acknowledged
   future rotation rather than the current sequencing blocker.
5. Documentation drift, BOM parsing, placeholder JSONL, and ProductClient scanner
   omission are supported by current files.

Output a Traditional Chinese Critical/Warning/Info report and a PASS/FAIL verdict.


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