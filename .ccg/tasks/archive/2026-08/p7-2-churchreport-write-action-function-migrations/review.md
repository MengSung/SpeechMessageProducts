# P7.2 Slice A 本機品質檢查

## 已驗證

- PowerShell contract：20 checks passed。
- `SpeechMessage.Dynamics.Tests`：493 passed、7 skipped、0 failed。
- `ChurchReport.MemberInfo.Tests`：404 passed、3 skipped、0 failed。
- Dynamics Release build：0 warnings、0 errors。
- ChurchReport Release build：0 warnings、0 errors。
- P7.2 live test 在未設定 opt-in 時明確 skipped。
- 預設 preflight：`outcome=no-go`、`reason=fixture-input-required`、`preflightOnly=true`、`operationExecuted=false`。
- `git diff --check` 通過；所有本輪修改文字檔均為 UTF-8 without BOM、CRLF-only、final CRLF。
- 沒有啟用 ChurchReport flag／流量，沒有執行 Official Worker、P6.2、CE 8.2 write、push、PR 或外部模型。
- `sunnyvalechback` CE 9.1 live preflight：`outcome=go`、`operationExecuted=false`、`featureFlagChanged=false`。
- Slice A 真實 Data8 evidence：`outcome=go`、`operationExecuted=true`、`sentinelState=confirmed`、`cleanupState=restored`、`featureFlagChanged=false`。
- 第一次把 Dynamics 與 ChurchReport process-boundary tests 平行執行時，ChurchReport 正確偵測到另一套測試暫時啟動的 `WorkerTestHost`；確認程序已退出後改為循序執行，兩套測試皆全綠。此項不以放寬斷言處理。

## 未完成／不宣稱完成

- P7.2 B-H slices、coverage validator、最後 commit/archive 與 P7.3 尚未開始。

## 外部 review

依使用者指示，本輪未執行 Gemini／Claude／CCG dual-model analysis 或 review；以上結論只依本機測試、建置、靜態與格式驗證。

## 2026-08-10 Slice C fresh-fixture dual-model review

- CCG self-healing reviewer run `20260810-233209-p7-2-slice-c-fresh-fixture-review-reviewer`
  completed with both Gemini and Claude outputs; neither backend was quota
  blocked or degraded.
- **Critical:** none. Both reviewers confirmed that
  `baseline-owner-unavailable` stops before ledger persistence or any CRM
  Create/Associate/Assign attempt. The focused provisioner test independently
  asserts `MutationAttemptCount=0` and an empty ledger for that branch.
- **Warning (accepted, no source change in this cycle):** the same fixed
  diagnostic vocabulary exists at the C# child and PowerShell parent boundary.
  The current validation is strict and fail-closed, but any future new category
  must update both sides and their contract coverage together.
- **Warning (environmental evidence gap):** the two true symlink-ancestor
  tests remain explicitly skipped because this Windows account lacks
  `SeCreateSymbolicLinkPrivilege`. The runtime ancestor guard is exercised by
  non-privileged gates, but the privileged test must run in an appropriately
  configured Windows verification environment before treating that attack path
  as fully covered.
- **Info:** `TryWriteDiagnostic` deliberately suppresses local diagnostic-file
  write failures so a child cannot turn a best-effort deidentified diagnostic
  into success, descriptor publication, cleanup authority, or a retry signal.
  The parent still returns its fixed fail-closed result.

## 2026-08-11 Slice C FreshPreflightProbe review status

- The required CCG self-healing reviewer run
  `20260811-091324-p7-2-slice-c-fresh-preflight-probe-review-reviewer` was
  started with both Gemini and Claude backends. It exceeded the user-authorized
  45-second wait limit and was stopped before a usable backend finding was
  produced.
- Status: **雙模型未完成**. This is not reported as a completed dual-model
  review or a single-model fallback. The local review evidence therefore remains
  the focused C# tests, 222 main PowerShell assertions, 623 fresh-fixture
  assertions, Release build, serial solution tests, strict source parser and
  byte-level encoding/scope checks.
- No Critical, Warning or Info model finding is available from this timed-out
  run. The next CE action remains one read-only FreshPreflightProbe only; the
  timeout does not authorize a write retry or a later Slice.

## 2026-08-11 Slice C weekly-report cardinality analysis

- Local source inspection proved that the fixed query contains all three filters:
  descriptor-bound transfer target list, `statecode=0`, and the fixed UTC Sunday.
  It does **not** count weekly reports belonging to other groups/lists on the same Sunday.
- The historical `not-exactly-one-active` evidence proves only that this exact
  intersection was not one valid active row. It cannot distinguish zero rows from
  two-or-more rows because its child classifier merges both cases.
- The CCG self-healing analyzer run
  `20260811-093944-p72-weekly-report-precondition-analysis-analyzer` completed
  Gemini output and was provider-quota blocked for Claude. Status: **雙模型未完成**,
  accepted single-model fallback only. Gemini agrees with the local query reading
  and identifies the merged category as a diagnostic/UX limitation, not proof that
  organization-wide Sunday reports are duplicates.
- Follow-up is limited to a TDD, zero-mutation diagnostic classification change.
  It must preserve the exact bounded query and strict evidence redaction; no CRM
  weekly report may be created, modified, deactivated, deleted, or selected by name.

## 2026-08-11 User-confirmed transfer semantics revision

- The user clarified the domain rule after the diagnostic analysis: zero active weekly
  reports for the exact target list and Sunday is normal; duplicate active reports is
  the error. Zero must not trigger automatic weekly-report creation or repair.
- Source inspection of the existing ChurchReport present-record builder shows the
  compatible behavior: an empty weekly-report ID omits the lookup but still creates
  the present record. P7.2 must preserve that behavior.
- When exactly one active report exists, the new present record must contain that exact
  lookup and read-back must verify it. When zero exists, read-back must instead prove
  that the lookup is absent. Duplicate/unavailable remains fail-closed before mutation.
- This changes the approved local implementation scope from classification-only to a
  tightly bounded TDD update of the Data8 transfer, fresh-fixture proof, probe, and
  strict evidence parser. The next CE operation remains a zero-mutation probe only.
- If Claude reports provider quota or session limitation, do not wait or retry it.
  Record `雙模型未完成`; use only a usable Gemini result as a degraded fallback, otherwise
  use local verification until a future review opportunity.

## 2026-08-11 Slice C weekly-report cardinality implementation review

- The CCG self-healing reviewer run
  `20260811-103426-p72-weekly-report-cardinality-review-reviewer` produced usable
  Gemini output. Claude was provider-quota/session blocked, so the run is an
  accepted **single-model fallback** and must not be described as a complete
  dual-model review. No retry was made.
- Gemini reported **no Critical, Warning, or Info findings**. It verified the
  exact bounded target-list/active-state/UTC-Sunday query, optional lookup
  read-back for `zero-active`, exact lookup read-back for `exactly-one-active`,
  fail-closed zero-mutation behavior for `duplicate-active`, and strict legacy
  category rejection.
- Local evidence independently confirms: focused Data8 test `40 passed`;
  focused fresh-probe/fixture test `36 passed / 1 explicit Windows
  symlink-privilege skip`; PowerShell contract `225 checks`; validator contract
  tests `6/6`; Release build `0 warnings / 0 errors`; and serial Release
  solution tests `1275 passed / 21 explicit opt-in or environment skips / 0
  failed`.
- The offline coverage validator remains the expected `no-go` for the four
  Slice C `matrix-evidence-pending` rows because it requires real CE evidence;
  this is not local implementation failure. The only next CE action is one new
  `-FreshPreflightProbe -Json` read-only invocation. It has zero mutation,
  ledger, descriptor-publication, feature-flag, traffic-switch, and cleanup
  authority.

## 2026-08-11 Slice C final live-state record

- The controlled `ExecuteFixture` cycle returned `no-go / child-process-failed` after the
  operation boundary, so its outcome is ambiguous and non-retryable. Read-only reconciliation
  returned `no-go / baseline-unprovable`; neither result supplies usable Slice C CE evidence.
- The subsequently authorized exact-ID cleanup lane returned `go / fresh-fixture-cleaned`.
  It removed the task-owned fresh fixture, descriptor and pending ledger; no fresh fixture
  remains and no weekly report was mutated.
- Final status: Slice C is **blocked**. Slice D–H must not start. A future cycle requires a
  separately governed external decision, not an automatic retry. The business rule remains:
  `zero-active` transfers with an absent weekly-report lookup, `exactly-one-active` must read
  back the exact lookup, and `duplicate-active`/`unavailable` stop before mutation.

## 2026-08-11 Continuation local quality check

- Read-only current-user control-plane check confirms no fresh source descriptor, list-management
  descriptor or fresh ledger remains after cleanup.
- Serial verification passed: P72 focused tests `102 passed / 2 explicit Windows privilege skips`,
  Data8 list-management tests `44 passed`, and strict PowerShell parser contracts `225 passed`.
  A parallel build first encountered a transient Microsoft Defender file lock on a shared Release
  intermediate output; the same checks pass serially, so no source change or CE action is indicated.
- This is local evidence only. It does not authorize a new Slice C CE cycle, an ExecuteFixture
  retry, weekly-report mutation, or Slice D–H.

## 2026-08-11 ExecuteFixture source-only audit

- `child-process-failed` is the parent's fixed nonzero-child category. It deliberately discards
  child stdout/stderr and refuses child evidence, so the historical result cannot safely expose
  or reconstruct a more specific execution reason.
- The conservative `operationExecuted=true` is not proof of a CE mutation. Since reconciliation
  did not prove a baseline and cleanup has completed, no local diagnostic can turn this previous
  cycle into verified CE evidence. Retrying or creating another fixture remains out of scope until
  a separately governed external decision authorizes an independent cycle.

## 2026-08-11 Independent-cycle preflight safety review — incomplete external models

- The user supplied the separately governed authorization for exactly one new independent Slice C
  cycle. The first allowed operation was the zero-mutation `FreshPreflightProbe`.
- CCG self-healing reviewer run
  `20260811-112657-p72-slice-c-independent-cycle-analysis-reviewer` used the user-approved
  45-second bound. Gemini timed out and its partial output is not accepted; Claude was blocked by
  provider quota/session limits. Therefore this is **雙模型未完成**, not a dual-model review and not
  an accepted single-model fallback. No provider retry was attempted.
- Local source tracing and the sanitized probe result independently establish
  `no-go / fixture-input-required`, `operationExecuted=false`, and no CE/credential/child-process
  boundary. The descriptor pair removed by deterministic prior cleanup cannot safely be rediscovered
  or replaced from stale state. No CE write, weekly-report change, or later Slice action is permitted.

## 2026-08-11 Seed bootstrap correction local review

- The circular local prerequisite was corrected to `legacy candidate -> read-only bootstrap proof ->
  permanent current-user seed -> fresh preflight -> fresh fixture -> Slice C -> read-back -> cleanup`.
  The seed has a strict, current-user-bound schema and contains only five static lists, a baseline
  leader, UTC Sunday, and fixed `crm91` / Data8 / CE 9.1 metadata. It excludes legacy target owner,
  old source contact, old expected relationship-list, credentials, endpoints, tokens, cookies, raw
  exceptions, and CRM responses.
- `-BootstrapFreshSeed` validates a fixed current-user `.bak` candidate before Credential Manager or
  child start. It uses the existing bounded `FreshPreflightProbe` read-only child and atomically
  publishes only after exact `go` evidence plus local read-back. Seed-missing/invalid/wrong-owner,
  existing seed, local publication failure, and non-go evidence fail closed with no ledger, active
  descriptor, feature flag, traffic change, or CE mutation.
- Local verification passed: runner contract `249` assertions; fresh-fixture contract `533`
  assertions; targeted C# preflight/provisioner tests `29` passed; Release build `0 warning / 0
  error`; serial Release solution tests `1263` passed with `21` explicit opt-in/environment skips;
  coverage-validator tests `6` passed. The direct coverage validator remains the expected
  `no-go / matrix-evidence-pending` for the four Slice C evidence rows.
- CCG self-healing review run `20260811-124836-p72-seed-control-plane-review-reviewer` was stopped
  at the user-approved 45-second bound. It produced only health/prompt artifacts and no usable
  Gemini or Claude findings. Status: **dual-model incomplete**; no retry was made and local review
  remains the current evidence.
- No real CE read or write, weekly-report change, feature flag, traffic switch, CE 8.2 operation,
  Official Worker operation, ledger publication, or descriptor publication was performed outside
  synthetic test roots. The prior cycle is not retried. The next permitted external action is one
  fresh bootstrap read-only proof, then only the ordered new Slice C cycle if every preceding gate is go.

## 2026-08-11 Seed-backed controlled CE cycle result

- Real bootstrap and fresh preflight completed `go` with bounded read-only evidence; both classified
  `weeklyReport=zero-active`, so no weekly-report lookup is required for a compliant transfer.
  Fresh provision then completed `go / fresh-fixture-provisioned` with the exact task-owned graph.
- The single allowed Slice C execution returned `no-go / child-process-failed` with
  `operationExecuted=true`. The parent intentionally refuses nonzero-child stdout/stderr/evidence,
  so no precise child cause can be reconstructed safely and this is not retry authority. The one
  read-only reconciliation returned `no-go / baseline-unprovable` and does not alter that conclusion.
- Exact-ID cleanup completed `go / fresh-fixture-cleaned`. Local read-back proves seed retained and
  active descriptor/ledger absence. No later Slice or further CE action is authorized. This is an
  external execution-evidence blocker, not a local build, control-plane, weekly-report, account,
  owner-selection, or cleanup failure.

## 2026-08-11 ExecuteFixture child failure diagnostic local review

- Source-only root cause: the live ExecuteFixture child writes strict bounded evidence, then its
  terminal xUnit assertion requires `outcome=go`. A valid child `no-go` therefore exits nonzero;
  the parent formerly discarded the strict file and reported only `child-process-failed`.
- The parent now reads a diagnostic only from its exact non-reparse temporary root and fixed
  `P72Data8ListManagementEvidence.json` name. It reuses the complete strict Slice C parser and
  exposes only allowlisted no-go categories (`runtime-failure`, `cleanup-failure`,
  `fixture-precondition-failed`, or `live-evidence-incomplete`). The terminal parent result
  remains `no-go / child-process-failed`; it accepts no operation evidence, success, read-back,
  cleanup, descriptor publication, feature-flag change, or retry authority from the child file.
- New regression coverage proves a nonzero child with `go` evidence exposes no category, while a
  valid strict `no-go / runtime-failure` file exposes that category only. `child-process-failed`
  and conservative execute ambiguity remain unchanged.
- Local verification: runner contract tests `258` passed; fresh-fixture contract tests `533`
  passed; `P72` targeted C# tests `102 passed / 2 explicit skips`; Release solution build passed
  with `0 warnings / 0 errors`; serial Release solution tests `1275 passed / 21 explicit skips`.
- Coverage-validator tests `6` passed. The direct validator remains the expected `no-go` with four
  `matrix-evidence-pending` Slice C rows, because no valid CE execution/read-back evidence exists.
- CCG self-healing review was launched with the user-approved 45-second bound. No usable Gemini
  or Claude finding was produced before the bound; status is **dual-model incomplete**. No retry
  was made. This local change authorizes no CE read/write or new Slice C cycle.

## Bug analysis: ExecuteFixture nonzero child lost its bounded no-go category

### 1. Root cause category

- **B — Cross-layer contract** and **D — Test-coverage gap**: a child test writes a strict
  no-go projection before its terminal assertion exits nonzero, but the parent had only a binary
  "nonzero means discard all evidence" rule.

### 2. Why the earlier handling did not help

- The prior fail-closed rule correctly denied success and retry, but it also erased the only safe
  category. It left future independently authorized diagnosis unable to distinguish a bounded
  runtime/cleanup/precondition/incomplete evidence state from an arbitrary child failure.

### 3. Prevention mechanisms

| Priority | Mechanism | Status |
| --- | --- | --- |
| P0 | Parent-owned-root + full strict-parser projection of one no-go category only | Implemented and tested |
| P0 | Preserve `child-process-failed`; deny success, operation/read-back, cleanup and retry authority | Implemented and tested |
| P1 | Match the handoff allowlist to the strict parser, including `live-evidence-incomplete` | Implemented and tested |

### 4. Systematic expansion

- Any future child-to-parent evidence lane must either require `ExitCode=0` for evidence use, or
  explicitly define a bounded diagnostic-only nonzero projection with the same parent-owned path,
  strict schema, redaction and no-authority rules.

### 5. Knowledge capture

- Updated `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` scenario
  `Child-process evidence trust and cleanup precedence`; no template mirror exists in this
  single-repo worktree.

## 2026-08-11 Zero-active fixture-store cross-layer correction review

### Root cause and contract decision

- Root-cause categories are **Cross-Layer Contract**, **Change Propagation Failure**, and
  **Test Coverage Gap**. Preflight, provision, and the product connector had already adopted the
  approved zero-or-one weekly-report contract, but the live fixture store still enforced exactly
  one. Consequently ExecuteFixture stopped before its mutation boundary with
  `fixture-precondition-failed`, and reconciliation retained the preceding `contact-owner-read`
  stage instead of identifying the composite transfer read.
- The corrected contract is closed and nullable: zero complete rows produce a request-local `null`;
  one complete row produces its exact identity; duplicate, paging, malformed, empty-identity, or
  unavailable projections fail closed before mutation. Zero-active never authorizes a weekly-report
  create, update, repair, selection, disable, merge, or delete.
- Zero-active present-record lookup intentionally excludes a weekly-report filter so a wrongly linked
  existing row cannot be hidden. Read-back and cleanup require the lookup to be absent. Exactly-one
  requires the exact lookup identity through graph read, reconciliation, and cleanup.

### Isolation, lifecycle, and performance review

- Resolver identities, SDK rows, queries, graph snapshots, and error categories remain method- or
  invocation-local. No request, user, session, tenant, profile, credential, connector, or mutable
  graph is stored in static state, singleton state, shared cache, background work, or diagnostic
  evidence. This prevents cross-request and cross-profile state retention.
- The transfer test double has one instance owner and deterministic disposal. It creates no timer,
  background task, cancellation registration, stream, process, WCF client, handle, connection, or
  unbounded collection. Cleanup performs only the exact proven Delete/Update/Remove sequence and
  rejects every unapproved mutation.
- Queries remain exact and bounded (`TopCount=2`, fixed target list, fixed active state, fixed UTC
  Sunday, fixed projection). The correction adds no scan, retry, global lock, cache, or per-request
  runtime construction, so it preserves the highest safe sustained performance while retaining the
  required isolation and fail-closed checks.

### TDD and local evidence

- RED: the fixture-store selection produced `12 passed / 3 failed`; the three failures were the new
  zero-active baseline, wrong-lookup visibility, and cleanup contracts against the old resolver.
- GREEN: fixture-store `16/16`; focused P7.2 `110 passed / 9 explicit skips / 0 failed`; Data8
  list-management `44/44`; PowerShell runner contracts `258`; fresh-fixture contracts `533`;
  coverage-validator tests `6/6`; Release build `0 warnings / 0 errors`; serial Release solution
  `1282 passed / 21 explicit skips / 0 failed`.
- The direct coverage validator remains an expected `no-go` only for the four Slice C
  `matrix-evidence-pending` rows. This is a live-evidence gate and not a local implementation,
  encoding, lifecycle, or compilation failure.

### CCG external review result

- Self-healing run
  `20260811-151229-p72-slice-c-zero-active-fixture-store-final-review-reviewer` completed Gemini with
  usable PASS output on both attempts and no Critical, Warning, or Info findings.
- Claude produced no usable output because the provider session quota was reached. The structured
  result is `degradedFallback=true`, `fallbackAccepted=true`, `quotaBlocked=true`, with Gemini as the
  only completed backend. This is recorded as an **accepted single-model degraded review**, not a
  complete dual-model review. The same review is not retried.

### Bounded release decision

- Refresh the final local test, byte-encoding, scope, feature-flag, and browser gates after the last
  test addition. If every gate remains Green, run exactly one final task-owned CE verification cycle.
- That cycle is terminal. Complete execution/read-back/cleanup evidence permits Slice C completion;
  any no-go, ambiguity, or cleanup uncertainty closes Slice C as unreleasable without another cycle
  and leaves Slice D-H closed. Weekly reports remain immutable in both outcomes.

## 2026-08-11 Traditional Chinese documentation completeness review

- The working-tree C# documentation audit covered 32 files with actual content differences and
  detected 23 undocumented public/internal declarations. All findings were confined to
  `WorkerFrameCodecTests.cs`, `WorkerEnvelopeCodec.cs`, and `WorkerEnvelopeValidator.cs`; after adding
  Traditional Chinese XML documentation, the same audit reported `FindingCount=0`.
- Documentation now states the real stream/buffer owner, maximum lifetime, bounded allocation,
  cancellation propagation, strict UTF-8 behavior, fail-closed ordering, deterministic Dispose, and
  cross-request/profile non-retention contract. Review found no claim that exceeds the actual code.
- Post-comment verification passed: Worker focused `21/21`; Release build `0 warnings / 0 errors`;
  serial Release solution `1282 passed / 21 explicit skips / 0 failed`.
- The final byte gate uses direct byte counting rather than regex inference. It passed 57 selected
  C#/task/spec/CCG artifacts with strict UTF-8, no BOM, CRLF-only, final CRLF, no U+FFFD, and no common
  mojibake marker.
- CCG run `20260811-154730-p72-traditional-chinese-comment-completeness-final-review-reviewer` returned
  Gemini PASS with Critical=0, Warning=0, Info=0. Claude was provider-session-quota blocked; the
  structured result is an accepted single-model degraded fallback, not complete dual-model success.
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs` cannot be modified in this worktree
  because commit `01fe6d001a6b44e4fc840bee68c1868d4dbc1dee` deleted the obsolete file on 2026-08-02.
  Reintroducing retired code merely to alter encoding would violate scope and the current Worker-based
  architecture; the replacement/current modified files are covered by the byte and documentation gate.

## 2026-08-11 最終 CE／瀏覽器終態審查

- 本輪唯一 final cycle 的去識別化證據已寫入
  `p7.2-slice-c-final-terminal-result.json`。順序與結果為：preflight go、fresh provision go、
  repair probe `already-repaired`、execute `child-process-failed/live-evidence-incomplete`、
  reconciliation `baseline-unprovable`、exact-ledger cleanup go。
- `ExecuteFixture` 的保守 `operationExecuted=true` 不被提升為成功證據；因 child handoff 與完整
  read-back 不可證明，Slice C 判定 `unreleasable-fail-closed`。沒有 retry、沒有新 cycle，Slice
  D–H closed，weekly report immutable。Cleanup 之後 static seed 保留，active descriptors 與
  ledger 均不存在，feature flags 維持 disabled。
- 瀏覽器已啟動並嘗試本機 ChurchReport URL。應用程式在 hosted preflight 因 embedded Dynamics
  composition configuration unavailable 而停止，故頁面不可達；這是安全 fail-closed 結果，
  不是 UI 通過。瀏覽器沒有讀取 cookie、local storage、password 或 session store。
- 終止規則已驗證：成功或 no-go 都是本任務的 terminal state；任何後續 CE 寫入必須另立任務、
  新治理與新 fixture，不得在本任務內重新建立週期。

### 最終新鮮驗證

- Release build `0 warnings / 0 errors`；串行 solution tests `1282 passed / 21 explicit skips /
  0 failed`；PowerShell contracts `258 + 533`；coverage validator tests `6/6`。
- Direct coverage validator 保留四筆 `matrix-evidence-pending`，因 Slice C 沒有完整 live artifact；
  這是本輪 terminal release gate，而不是允許 retry 的工作清單。
- 第一次非串行 solution test 的單一 process-boundary failure 是跨測試專案的 WorkerTestHost
  觀察窗競爭。測試回合後原 PID 與所有同名程序均已結束；改以 `-m:1` 串行後完整通過，
  沒有以重試掩蓋產品 failure，也沒有遺留 process/resource。
