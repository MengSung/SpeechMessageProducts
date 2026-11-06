# CCG reviewer Task: p112-audit-probe-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# P1-12 guarded audit completion and data probe review

Read only Deployment/Schema/*.cs, Deployment.Tests/Schema*.cs and d365/schema/*.json. No secrets, database backups, raw artifacts, live connections or file mutations.

Live D3659.1 created all 36 P1 fields and 4 Active keys but ignored Lookup.IsAuditEnabled in CreateOneToMany for the two new_accountid lookups of the two new audited tables. Other projected constraints match. First apply correctly failed the strict post-publication check. We kept the strict assertion and reproduced the two audit mismatches in the SDK fake.

Creation now enables audit with UpdateAttribute only for the exact SDK ID returned by this invocation's new lookup create, transmitting identity and IsAuditEnabled=true only. Existing metadata still conflicts.
For the already partially completed run, CompleteCreatedAudits requires the reviewed original plan plus its before snapshot, revalidates the whole plan hash against that before, checks the same organization, permits only lookup fields absent before and explicitly CreateLookup in the original plan of a newly audited table, and runs the full planner on a candidate correcting only audit. Any other mismatch or missing component rejects before writing. It compares published/unpublished affected-table constraints, flushes before/target-ID evidence, enables only the identified new lookup audits, publishes affected tables and re-verifies. No generic repair/update, security change, type/date conversion or deletion exists.

Schema smoke uses fixed repo fixture Org.Level.Group/*/synthetic group text. It requires published verify and all keys Active. Candidates have operation-owned database GUIDs checked absent before Create and recorded/flushed before writing. Create must confirm the same database ID. Only official 0x80060892 DuplicateRecordEntityKey counts as expected rejection. It handles lost Create responses, precise ID-and-marker cleanup, cancellation and cleanup failure; other records with the same display name remain. SDK lease/lock stay with CLI. Review correctness, provenance limitations, drift, ownership, error preservation and cleanup. Report Critical/High/Warning/Info.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
