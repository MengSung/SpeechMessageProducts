# Deployment CLI Contract

## Scope and connection boundary

P0-12 is an offline implementation and test package. No D365 or LINE connection
is permitted until the user reports goodshepherdback cleanup complete. Earlier P0-13 guidance reserved writes for the user. On 2026-11-04 the user
explicitly authorized Codex to finish this goodshepherdback trace acceptance,
including smoke, the reviewed disable plan/apply, verification and restoration of
exception-only tracing. This authorization is scoped to that acceptance and does
not authorize other organizations or future LINE pushes. Credentials come only from the Deployment User
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

P0-13 diagnostic correction (2026-11-04): after reporting the original exception
once, the CLI may print only `ErrorCode` from a typed
`FaultException<OrganizationServiceFault>` and its SDK `InnerFault` chain. Format
each Int32 as eight uppercase hexadecimal digits with invariant culture, and
iterate at most four nodes including the outer detail, even for a cyclic chain.
A missing detail prints no codes. Never read or serialize Message, Reason,
TraceText, ErrorDetails, Data or raw stack for this supplemental output. Its
failure must not prevent original reporting or change exit 1. Keep command-local
lease disposal and caller-cancellation classification unchanged. Offline fault
injection must cover secret-bearing fields, reporter failure, deep/cyclic chains,
missing detail, release-before-report and actual shared-log redaction.

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

## Step secure-configuration lookup (P0-13 correction, 2026-11-04)

Use the official `sdkmessageprocessingstepsecureconfigid` logical attribute on
`sdkmessageprocessingstep`; `secureconfigid` is not an alias. Queries, creates,
updates, presence checks, export and fingerprints share one product constant.
The separate secure-configuration entity still uses `secureconfig` for its
content; do not retrieve that content during plan, verify or export.

The offline SDK double must independently validate step column names and filter
attributes, plus create/update attribute keys, against a fixed official schema.
Never derive this schema from product constants. Exercise a fresh registration
followed by verification, existing secure lookup export/cleanup and trace/push
transitions. A first-install-only test misses queries that start only after a
Plugin type exists. The real CLI lifecycle test uses a unique temporary solution,
a generated unsigned net462 PE and an exclusively scheduled working-directory
scope restored in finally; every command releases its injected fake lease.

Primary references: Microsoft Learn `sdkmessageprocessingstep` entity reference
(`SdkMessageProcessingStepSecureConfigId`) and web-service error codes
(`QueryBuilderNoAttribute`, `0x80041103`). A live retry requires explicit user authorization; successful
offline checks do not establish the current organization's state.

## Offline verification

Keep all original 17 test methods and every subsequently added case; 17 is the original contract list, not the current suite total. L1 uses operation-local in-memory SDK services;
L2 emits an unsigned net462 PE into the test's unique temporary folder and reads
metadata without Assembly.Load. A test runner may itself target net48, but the
generated deployment fixture must exercise the net462 Plugin metadata contract.
Dispose readers, timers, locks and temp fixtures.
CI includes the Deployment project and a separate >=80% coverage report.
Actual WCF/WS-Trust connectivity and D365 sandbox behavior remain P0-13 user
acceptance, not a claim made by the offline suite.

## Minimal P0-13 acceptance (user approved 2026-11-04)

P0-13 is user-approved trace-only sandbox acceptance; the user explicitly
authorized Codex to finish the remaining live operations on 2026-11-04. The Plugin deployment
assembly targets .NET Framework 4.6.2 per the user's 2026-11-04 D21
authorization. Registration, trace smoke and disabled-state verification on the
target D365 host were confirmed on 2026-11-04. The smoke field `FrameworkFileVersion=4.8.x` describes
the server's loaded `mscorlib` runtime file, not the Plugin target framework.
Only OrganizationUrl,
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
full-solution local checks. The 2026-11-04 live run passed all five trace checks, reported runtime
FrameworkFileVersion 4.8.4795.0, left zero run-scoped smoke tasks, disabled the
diagnostic step with a single UpdateStepState plan and verify=0, and restored
exception-only tracing (value 1). Keep only fixed results, counts and validated
versions as evidence; never persist or print the raw message block. The temporary
audit uses the existing fixed-endpoint connector, owns and disposes its
configuration and connection, and queries only the exact diagnostic type, run
time window and correlation. Old LINE data was retained under the user's explicit
P0-only exception; do not describe it as cleared or extend that exception to later
phases. This acceptance did not exercise LINE delivery or prove hosted CI success.

## P2-04 前置失敗後的原況核對（2026-11-08）

- `export.Assembly.Content` 是 Base64；必須解碼後比較 DLL 位元組數與 SHA-256。
  字串 964608 字元與解碼後 723456 bytes 是同一份內容，不能因此誤判部分更新。
- `apply` 回 `0x8004852E` 時先停止寫入，核對 live assembly、同一 step ID、
  trace／secure lookup／enabled 與 pre-apply export；不盲目 rollback 或重跑。
  Microsoft 官方名稱是 `SqlEncryptionSymmetricKeyPasswordDoesNotExistInConfigDB`。
- 本次 goodshepherdback 唯讀 `IsDataEncryptionActive` 為 false，
  `RetrieveDataEncryptionKey` 回空；這不是 LINE token 的格式錯誤。
  未取得原有組織金鑰或可回復的核准方案前，不生成替代鍵、不重設加密、不讀寫正式組織。
- 本次已核對原 DLL hash 未變、步驟仍停用／trace／無 secure，trace 2→1 並讀回。
  D34 已清理的非測試主 LINE ID 不因部署失敗而恢復；備份保留在 repo 外 DPAPI。
  這些結果不等於 smoke、TLS、整體 verify=0 或 P2-04 前置通過。

## P2-04 加密解除後的接續證據（2026-11-08）

前節 false 是先前失敗時的歷史狀態，不是目前狀態。SDK `IsDataEncryptionActive` 的回應欄位為 `Results["IsActive"]`；先驗 Contains 與 bool 型別再使用，缺欄位不能當 false。此次已為 true，未讀取／輸出組織加密金鑰。

兩輪推播均獲真實 LINE 200，但 smoke 的外部診斷契約仍未達成。收尾 verify=0、追蹤=1、同一步驟 trace／停用／空 secure，依精確 trace ID＋correlation 與 owning step 備份後只清掉自己的兩個 trace／已完成作業；不得清其他作業或平台步驟。手機收訊與 HTTPS 成功不等於獨立量測 OS TLS 協定版本，也不能取代 smoke=0。

本機診斷腳本使用 PowerShell 對 SDK 查詢時，管線 `object[]` 可能含 PSObject 包裝，導致 DataContract 序列化失敗。對 GUID 的 In 條件建立 `ConditionExpression`，逐一 `Values.Add(Guid.Parse(...))`，先離線序列化確認；不要把此失敗誤判為伺服器資料錯誤。live CLI 與會改寫其 bin 的建置不可並行；改用獨立輸出或等 lease／程序釋放後重建。
