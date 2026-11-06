# CCG reviewer Task: p111-schema-published-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# Final published metadata verification review

Read only the P1 schema source/tests, d365/schema/speechmessagecore.schema.json, and .trellis/spec/backend/schema-deployment-contract.md. Do not access secrets, backups, logs with personal data or other task directories. No code mutations or live connections.

Review full P1-11 implementation including the latest change: SchemaDeploymentCli verify now invokes SchemaDeploymentEngine.Verify which requests published metadata (RetrieveAsIfPublished=false). Plan still reads unpublished candidates. A regression simulates the published account missing new_defaultlanguage despite the unpublished field existing, requires exit 1 and no SDK writes, and validates lease drain. The fixture independently requires the source field before hiding it. Confirmed original CLI gave exit 0; all 117 Deployment tests pass after the fix.

Check correctness, bounded lifecycle, organization separation, immutable complete metadata, real server IDs, strict JSON/hash drift, additive-only writes, previous components preservation, pending keys, before-file flush and safe shared exception reporting. Distinguish local fake SDK tests from the real readonly metadata export/88-operation plan. Live apply has not occurred. Report Critical/High/Warning/Info with concrete evidence.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
