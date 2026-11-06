# Final published metadata verification review

Read only the P1 schema source/tests, d365/schema/speechmessagecore.schema.json, and .trellis/spec/backend/schema-deployment-contract.md. Do not access secrets, backups, logs with personal data or other task directories. No code mutations or live connections.

Review full P1-11 implementation including the latest change: SchemaDeploymentCli verify now invokes SchemaDeploymentEngine.Verify which requests published metadata (RetrieveAsIfPublished=false). Plan still reads unpublished candidates. A regression simulates the published account missing new_defaultlanguage despite the unpublished field existing, requires exit 1 and no SDK writes, and validates lease drain. The fixture independently requires the source field before hiding it. Confirmed original CLI gave exit 0; all 117 Deployment tests pass after the fix.

Check correctness, bounded lifecycle, organization separation, immutable complete metadata, real server IDs, strict JSON/hash drift, additive-only writes, previous components preservation, pending keys, before-file flush and safe shared exception reporting. Distinguish local fake SDK tests from the real readonly metadata export/88-operation plan. Live apply has not occurred. Report Critical/High/Warning/Info with concrete evidence.
