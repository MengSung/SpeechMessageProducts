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
values and therefore is not a credential-rotation tool. Both CLI and Markdown
must disclose this scope whenever placeholders exist. If metadata matches but
secret placeholders exist, verify returns 3 (incomplete), never full success.
Metadata differences still return 1; trace-only complete equality returns 0.
Do not add secret resolution, hashes of actual secrets, new rotation commands
or raw exports to obtain a successful verification result. Secret-value
changes require separately authorized rotation work and user test delivery.

A bounded OS file-handle lease serializes same-organization write commands on
this machine; release it after the connection is disposed. Smoke only creates
its own marked task, polls its correlated async/trace metadata for up to three
minutes and deletes the task in finally. Caller cancellation is not notified.

## Offline verification

Keep all original 17 test methods and every subsequently added case; 17 is the original contract list, not the current suite total. L1 uses operation-local in-memory SDK services;
L2 emits an unsigned net48 PE into the test's unique temporary folder and reads
metadata without Assembly.Load. Dispose readers, timers, locks and temp fixtures.
CI includes the Deployment project and a separate >=80% coverage report.
Actual WCF/WS-Trust connectivity and D365 sandbox behavior remain P0-13 user
acceptance, not a claim made by the offline suite.

## Minimal P0-13 acceptance (user approved 2026-11-04)

P0-13 is user-operated trace-only sandbox acceptance. Only OrganizationUrl,
Username and Password under the Deployment organization User Secrets are needed;
no test LINE identity or token is required. Plugins and Deployment have no direct
or transitive ImageSharp dependency, so these project builds need no Six Labors
license. A manifest explicitly enabling goodshepherdback creates the new step
in enabled state; one plan/apply/verify=0 registration pass is sufficient.
Always disable the step with a new plan/apply/verify=0, restore exception-only
tracing and confirm smoke task cleanup afterwards.

Sandbox push acceptance moves to the prerequisite of P2-04. Own Plugin FileVersion
tracing and update/rollback acceptance move to phase 5 before the first production
Plugin is deployed to goodshepherdback. P0 live same-event retry is replaced by
the source-record-ID retry-key unit contract and LINE's 24-hour retry-key validity
contract, as accepted by the user; this is not a claim of observed delivery or
an assurance beyond the retry-key validity window. Transitional CI scope is
accepted; prioritize DevExpress source resolution once phase 1 begins and retain
full-solution local checks. Cleanup and real P0 acceptance remain unreported.
