ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p112-recovery-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# P1-12 recovery prerequisite review

Assess only the local script `artifacts/p1-12-20261106/Prepare-P1RecoveryEvidence.ps1` and `.ccg/tasks/p112-live-schema-acceptance/plan.md`.
Do not access User Secrets, credential files, database backups, raw data or other artifacts. Do not connect to anything or execute the script.

The owner authorizes necessary bounded test work in the explicitly designated development-copy organization. A shared VM also hosts other organizations: no VM restore, restart, account/domain/security-policy changes, or changes to other databases are allowed.
We have matched the live API organization ID to the VM's SQL configuration. We need a new COPY_ONLY CHECKSUM compressed full backup and an actual restore into a new GUID-named, restricted, unregistered database with unique files, followed by physical integrity verification and strictly owned cleanup.

Review the script for identity/path constraints, no overwrite or REPLACE, backup-chain preservation, secret safety, deterministic resource disposal, scope of DROP DATABASE, and fail-closed cleanup. Report Critical/High/Warning/Info with concrete lines. This is prospective analysis, not evidence that any backup or live acceptance passed. Do not mutate any files.


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