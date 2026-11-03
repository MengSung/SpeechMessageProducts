# Deployment CLI Contract

## Scope and connection boundary

P0-12 is an offline implementation and test package. No D365 or LINE connection
is permitted until the user reports goodshepherdback cleanup complete. Afterwards,
Codex may only run the read-only commands authorized by D24; apply, rollback and
smoke remain user operations. Credentials come only from the Deployment User
Secrets provider; Windows Credential Manager is a future option. Do not modify
ToolUtility or accept passwords on the command line.

The connector validates the sole allowed HTTPS host and Organization.svc path,
then uses CrmConnectionService.CreateOnPremiseClient directly, without a shared
pool. One command owns one OrganizationLease. Dispose releases the client and
clears retained references, including on failure or caller cancellation.

## Metadata, output and mutation

The registration manifest contains templates only. Plan and Markdown never
resolve secrets. Export contains managed assembly content and metadata summaries;
settings use presence bits or hashes. Never export trace text, token or userId.
Report real exceptions through the shared diagnostic owner, which writes only
safe type/stack metadata; never print raw exception messages or CLI values.

Plan hashes cover manifest, DLL and current organization fingerprint. Apply
revalidates these, exports and flushes the current state before writing. Stable
step identities use type/message/entity/stage/mode/rank, and duplicate database
IDs fail closed. Existing step configuration changes update the original ID;
trace/push transitions retain idempotency, parse secrets only for writes, and
remove obsolete secure configuration records. Plan does not compare secret
values and therefore is not a credential-rotation tool.

A bounded OS file-handle lease serializes same-organization write commands on
this machine; release it after the connection is disposed. Smoke only creates
its own marked task, polls its correlated async/trace metadata for up to three
minutes and deletes the task in finally. Caller cancellation is not notified.

## Offline verification

Keep all original 17 test methods. L1 uses operation-local in-memory SDK services;
L2 emits an unsigned net48 PE into the test's unique temporary folder and reads
metadata without Assembly.Load. Dispose readers, timers, locks and temp fixtures.
CI includes the Deployment project and a separate >=80% coverage report.
Actual WCF/WS-Trust connectivity and D365 sandbox behavior remain P0-13 user
acceptance, not a claim made by the offline suite.
