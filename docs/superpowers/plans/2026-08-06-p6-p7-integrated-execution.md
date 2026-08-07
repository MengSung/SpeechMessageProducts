# P6＋P7 Data8-first Continuous Trellis Execution Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` in Codex inline mode. Steps use checkbox (`- [ ]`) syntax for tracking. Do not dispatch implement/check sub-agents.

**Goal:** 以一次可續跑的 `/goal` 授權，先完成既有 P6.1 Official Worker Router／Pool／Lease 擴充點的文件與結案 gate，再以 Data8 完成 P7.0～P7.5，使 ChurchReport 在 Lenovo Legion 同時支援 `Embedded + Data8` 與 `DedicatedGateway + Data8`，並移除 ChurchReport 的 ToolUtility／CRM SDK production dependency。

**Architecture:** 這是一個 operator-assisted、可跨對話續跑的 umbrella plan，不是一個不可驗證的巨型 change set，也不是完全無人值守批次。Official Worker 的 P6.2 真機相容性保留為 `evidence-pending` 的未來獨立支線，不是本 Goal 的 predecessor gate。代理先完成 P6 文件／quality／spec／archive，再啟動既有 P7.0；P7.0 產生 capability matrix 後，才為每個必要 write／Action／Function family 建立 fixture owner、cleanup 與 reconciliation gate。接著代理依序管理 P7.1～P7.5 children；每個 child 各自完成 Trellis planning、activation、implementation、quality gate、spec update、task-owned commit 與 archive。P8 是第一個 ChurchReport `CentralGateway + Data8` 雲端部署的獨立後續 Goal，本計畫不得啟動 P8。

**Tech Stack:** .NET 10／ASP.NET Core、net48 Official CRM 8.2／9.1 Workers、Data8 Connector、Trellis task workflow、PowerShell 5.1 deployment/readiness tools、xUnit、Visual Studio 2026、Lenovo Legion local development environment。

**Feasibility verdict:** **Go for the rebaselined offline path**。P6.1 的離線 Router／Pool／Lease 證據已具備；P6.2 最新結果只表示 Official Worker live compatibility 尚未驗證，不阻塞 Data8-first P7。`sunnyvalechback` 只證明 CE 9.1 有隔離的開發環境與 test-member 可行性，不代表 P7.2 所有 operation family 已獲任意寫入授權。

> **2026-08-07 scope supersession:** 本文件後方保留的舊 P6.2 operator bridge／live matrix
> 步驟是歷史參考，不得在本 Goal 執行。現行順序是 `P6.1 offline closure → P7.0 →
> P7.1～P7.5`；Official Worker live evidence 只有在未來另立 deployment task 並明確選用
> Official Worker 時才恢復。

**Authority:** 本文件與其引用的 Trellis task artifacts／`.trellis/spec/` 是 P6／P7 執行主軸。`2026-08-06-claude-*.md` 只作輔助稽核；若其「環境阻塞全部解除」、「可自由寫入」或其他敘述與本文件衝突，不得用來放寬 gate。

---

## P6／P7 responsibility split

| 階段 | 必須證明的契約 | `sunnyvalechback` 的影響 |
|---|---|---|
| P6 | deployment-owned ConnectorKind／CE version routing、Official Worker process／IPC、Router／Pool／Lease／admission、IFD credential boundary、generation drain 與資源回收；P6.1 離線 gate 是本次結案範圍 | Official Worker live compatibility 保留為 `evidence-pending`；不取消 P6，也不授權業務寫入 |
| P7.0 | 從 70-row source matrix 產生 capability／support／evidence matrix，逐項決定 CE 8.2／9.1 是 `required`、`unsupported` 或 `evidence-pending` | 將它記為 CE 9.1 safe-write candidate environment，但不預先假設每個 capability 都可安全測試 |
| P7.2 | 驗證 ChurchReport 的 write／Action／Function 業務語意、idempotency、authoritative writer、timeout-after-commit、fixture ownership、cleanup 與 reconciliation | 只可操作 matrix 核准且有 test-owned fixture 的 operation family；「可新增一筆測試會員」不得擴張成任意 financial／appointment／destructive writes |

因此 P6 與 P7.2 不是重複工作。P6 是 connector／version／lifecycle 底座；P7.2 才是產品業務寫入契約。

## Continuous-execution contract

使用者的一次 Goal 授權包含：

- 從既有 `.trellis/tasks/08-05-official-worker-router-ce-integration` 的 P6.1 結案 checkpoint 續作；不重開 P5、不重做已綠的 P6.1，也不重跑 P6.2 startup。
- 在 predecessor gate 全綠時建立、規劃並啟動 P7.1～P7.5 child tasks，不需逐階段再次要求「PROCEED」。
- 允許修改 P6／P7 所需產品程式、測試、task artifacts 與本機開發設定；允許 Lenovo 上 capability-level feature gate 與 ChurchReport local traffic cutover。
- P6 本輪不執行 CE operation；P7 write/action/function 的 live evidence 只限明確核准的非正式環境或 test-owned fixture，且必須有 cleanup／reconciliation。
- 允許對每個完成的 child 建立 task-owned local commit 並執行 Trellis archive；不得把不相關使用者變更納入 commit。
- 不執行 Gemini／Claude 或 CCG external model runner；review 使用本機 Trellis check、tests、build、static scan、stress／soak 與 evidence inspection。
- 不 push、不建立 PR、不部署雲端、不啟動 P8、不操作第二／第三產品。

「一次完成」表示使用者不必反覆下提示詞；它不取消技術順序、fail-closed gate、真機證據或安全邊界。

Trellis task 拓樸不是單一 parent tree，執行不得依賴 children traversal：

| 階段 | 明確 task path／建立方式 | Parent |
|---|---|---|
| P6 | `.trellis/tasks/08-05-official-worker-router-ce-integration`（目前 active） | `08-04-dynamics-connection-management-plan` |
| P7.0 | `.trellis/tasks/08-05-gateway-capability-inventory`（P6 封存後直接 `task.py start`） | `08-05-gateway-purpose-and-positioning` |
| P7.1～P7.5 | 以本文件固定 slug 逐一 `task.py create --parent 08-05-gateway-purpose-and-positioning` | `08-05-gateway-purpose-and-positioning` |

代理必須使用上表的明確路徑切換 task；不得從 P7 parent 的 children 推測或尋找 P6。

## Operator PowerShell bridge

當代理不能直接存取 D365 主機、Credential Manager、特定 Windows service identity 或未來遠端環境時，不把整個 task 交回使用者。固定流程是：

1. 代理先完成所有 repository 可完成的程式、測試與靜態檢查。
2. 代理在 `docs/scripts/` 建立 task-specific PowerShell 與對應 tests；script 必須 bounded、fail closed、Windows PowerShell 5.1 相容、不得讀出或輸出 secret blob。
3. 代理在 task-local `operator-handoff-*.md` 寫清楚執行主機、Windows identity、逐步命令、預期 sanitized schema 與停止條件。
4. 使用者只貼回去識別化 JSON／文字結果；不得貼密碼、token、cookie、connection string、private key 或完整個資 payload。
5. 代理驗證結果後自動從原 checkpoint 續跑；只有結果揭露新的真實 blocker 才再次產生更小的 handoff。

此 bridge 僅適用於未來獨立的 Official Worker deployment 或 P7 CE fixture evidence；本輪 P6
文件／quality／本機檔案工作不得要求使用者代做，也不得重建已完成的 profile。

---

### Task 0: Historical G0 feasibility record（已完成；目前 Goal 不執行）

> 2026-08-07 範圍重校後，原 G0 的 profile／Credential Manager／readiness
> operator bridge 已完成並保存於 P6 task-local evidence。它不是目前 P6 closure
> 的執行步驟，也不是 P7 的 live prerequisite。不要重新建立 profile、重跑 readiness
> handoff、啟動 Official Worker 或呼叫 CE。若日後明確選用 Official Worker，必須另立
> deployment task 並產生新的 handoff。

歷史來源：

- `.trellis/tasks/08-05-official-worker-router-ce-integration/p6-p7-execution-baseline.md`
- `.trellis/tasks/08-05-official-worker-router-ce-integration/operator-handoff-p6.2.md`
- `.trellis/tasks/08-05-official-worker-router-ce-integration/p6.2-ce-readiness-evidence.md`

目前仍須記錄的非機密事實只有：P6.1 離線 gate 已通過、Official Worker live
compatibility=`evidence-pending`、`sunnyvalechback` 是隔離的 CE 9.1 開發環境，且
P7.2 任何寫入都必須等 capability-specific fixture／cleanup gate。密碼、token、cookie、
Organization ID、完整 endpoint 與原始例外不得複製到本計畫。

---

### Task 1: Resume and protect the current P6 checkpoint

**Files:**
- Read: `.trellis/tasks/08-05-official-worker-router-ce-integration/prd.md`
- Read: `.trellis/tasks/08-05-official-worker-router-ce-integration/design.md`
- Read: `.trellis/tasks/08-05-official-worker-router-ce-integration/implement.md`
- Preserve: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.ps1`
- Preserve: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1`
- Preserve: `docs/superpowers/plans/2026-08-05-official-worker-deployment-readiness-probe.md`

- [ ] **Step 1: Load Trellis and task context**

Run:

```powershell
python .\.trellis\scripts\get_context.py
python .\.trellis\scripts\get_context.py --mode phase
git status --short
```

Expected: current task is `official-worker-router-ce-integration`, status is `in_progress`, the committed readiness-probe scripts and current Worker artifact generation exist, and the historical G0 record is present. Do not start a Worker or contact CE.

- [ ] **Step 2: Re-run the focused readiness-probe tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1
```

Expected: `All official Worker deployment readiness probe tests passed.`

- [ ] **Step 3: Confirm P6.1 remains green without rewriting it**

Run the P6 focused offline test set from the task `implement.md`, then the full Dynamics tests and Release build. Any regression is fixed in P6-owned files before proceeding; existing P6.1 evidence is not discarded merely because P6.2 is incomplete. This step must not start a Worker or issue a CE operation.

### Task 2: Historical P6.2 deployment-readiness record（目前 Goal 不執行）

> 本節是歷史索引，不是待辦清單。2026-08-06 的 readiness／profile／overlay
> operator bridge 已完成並保存在 P6 task artifacts；2026-08-07 重校後，
> Official Worker live compatibility 仍為 `evidence-pending`。目前 Goal **不得複製、
> 貼上或執行**本節曾使用的 PowerShell，不得重新建立 profile、publish overlay、
> 啟動 Worker 或呼叫 CE。未來若明確選用 Official Worker，必須建立新的獨立
> deployment task，重新核准輸入與輸出目錄。

**歷史資產（只讀）：**

- `artifacts/dynamics-workers-p6.2/official-worker-manifest.json`
- `docs/scripts/Publish-DynamicsOfficialWorkers.ps1`
- `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.ps1`
- `docs/scripts/New-DynamicsOfficialWorkerDeployment.ps1`
- `.trellis/tasks/08-05-official-worker-router-ce-integration/operator-handoff-p6.2.md`
- `.trellis/tasks/08-05-official-worker-router-ce-integration/p6.2-ce-readiness-evidence.md`

歷史命令區塊已從本執行計畫移除，避免把「readiness `go`」誤讀成「Worker／CE
真機相容性 `go`」。這些資產不可作為目前 P6 closure 或 Data8-first P7 的 gate。

### Task 3: Historical P6.2 read-only CE evidence（目前 Goal 不執行）

> CE matrix、WhoAmI、connection validation、live harness 與任何 write/action/function
> evidence 都延後到未來獨立 Official Worker deployment task。P6 本次只以 P6.1
> offline closure 結案；`sunnyvalechback` 的隔離 test member 也不會在本階段被操作。

**未來 task 的必要前置（僅供規劃，不得在本 Goal 執行）：**

1. 由 deployment owner 明確選定 Official Worker 與 CE profile。
2. 建立 bounded、allowlisted、去識別化的 operator handoff；credential values
   必須留在核准的 secret provider，不能進入 repo 或對話。
3. 先完成 offline profile／IPC／lifecycle checks，再由 owner 核准 read-only CE
   matrix、drain／baseline 與 rollback；缺少任一外部權限時停止，不以盲目重試繞過。
4. 另立 task，另行完成 quality、spec update、task-owned commit 與 archive。

本節沒有目前 Goal 可執行的命令。下一個可執行步驟是 Task 4 的 P7.0 activation，
且只能在 P6 完成其既定 closure、commit 與 archive 後進行。

### Task 4: Activate and complete P7.0 inventory／coverage gate

**Files:**
- Start: `.trellis/tasks/08-05-gateway-capability-inventory/`
- Read: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json`
- Update: `.trellis/tasks/08-05-gateway-capability-inventory/preliminary-capability-inventory.json`
- Create: `docs/scripts/Test-DynamicsCapabilityCoverage.ps1`
- Create: `docs/scripts/Test-DynamicsCapabilityCoverage.Tests.ps1`
- Update: `.trellis/tasks/08-05-gateway-capability-inventory/prd.md`
- Update: `.trellis/tasks/08-05-gateway-capability-inventory/design.md`
- Update: `.trellis/tasks/08-05-gateway-capability-inventory/implement.md`

- [ ] **Step 1: Activate the existing task**

Run:

```powershell
python .\.trellis\scripts\task.py start .\.trellis\tasks\08-05-gateway-capability-inventory
```

Expected: P7.0 becomes `in_progress`; no P7.1～P7.5 child exists yet.

- [ ] **Step 2: Refresh source-derived manifests**

Recompute the 70-row source hash and scan Registry, Data8 executor, Official Worker protocol/Router, ProductClient, consumer gates and ChurchReport ToolUtility／CRM SDK references. Preserve separate declared／implemented／enabled／real-evidence states.

- [ ] **Step 3: Implement the deterministic validator test-first**

The test suite must prove fixed sorting, byte-stable JSON, stable rule IDs, nonzero failure exit, bounded input, no network access and rejection of unclassified rows, missing owner/DTO, invalid IDs, unknown connector/CE states, generic CRUD/FetchXML, missing lifecycle ownership and unowned legacy dependencies.

- [ ] **Step 4: Produce the executable P7.1～P7.3 work queue**

For every capability family, assign the canonical phase, exact source/test file ownership, DTO/ProductClient/Registry/executor owner, CE support target, rollout/rollback owner and evidence gate. Capability families with different transaction or rollback owners become separate child tasks even when they share an entity. For every matrix-required P7.2 family, also emit the exact fixture owner, allowed mutation set, cleanup/reconciliation contract and ambiguous-timeout rule; the environment-level `sunnyvalechback` confirmation alone is not a green operation-family gate.

- [ ] **Step 5: Close P7.0**

Run validator tests, JSON parsing, encoding/CRLF checks and `git diff --check`; perform Trellis check/spec judgment, commit only P7.0-owned changes and archive the task.

### Task 5: Complete P7.1 read capabilities

**Files:**
- Create via: `python .\.trellis\scripts\task.py create "P7.1 ChurchReport read capability migrations" --slug churchreport-read-capability-migrations --parent 08-05-gateway-purpose-and-positioning`
- Modify by matrix ownership: `SpeechMessage.Dynamics.Abstractions/Operations/`
- Modify by matrix ownership: `SpeechMessage.Dynamics.Connectors.Data8/`
- Modify by matrix ownership: `SpeechMessage.Dynamics.WorkerHost/` and versioned Worker adapters when required
- Modify by matrix ownership: `SpeechMessage.Dynamics.ProductClient/`
- Modify by matrix ownership: ChurchReport read consumers identified by the P7.0 matrix
- Test: `SpeechMessage.Dynamics.Tests/`
- Test: `SpeechMessage.Dynamics.Crm82Worker.Tests/`
- Test: `SpeechMessage.Dynamics.Crm91Worker.Tests/`
- Test: `ChurchReport.MemberInfo.Tests/`

- [ ] **Step 1: Create, plan and activate the P7.1 child under the parent**

Use the fixed slug `churchreport-read-capability-migrations`, write complete PRD/design/implement artifacts from the P7.0 queue, then start it under the integrated Goal authorization.

- [ ] **Step 2: Deliver Package01 as the template vertical slice**

Write fail-first contract, authorization, connector-support and lifecycle tests; implement bounded DTO, catalog/Registry, executor, ProductClient and feature gate; obtain CE evidence and legacy parity before enabling the local consumer.

- [ ] **Step 3: Deliver every remaining read family**

Repeat the same template one rollback-owned slice at a time. Shadow reads share a bounded deadline and never change the authoritative response; all response buffers, tasks, timers, cancellation registrations, permits and leases are released. Real CE evidence is required only for connector／version combinations marked `required` by P7.0; `unsupported` combinations are fail-closed matrix outcomes, not failed live tests.

- [ ] **Step 4: Close P7.1**

Validator, focused tests, full Dynamics/Worker/ChurchReport tests, Release build, parity, p50/p95/p99, soak/lifecycle, encoding and diff gates must all pass before task-owned commit/archive.

### Task 6: Complete P7.2 write／Action／Function capabilities

**Files:**
- Create via: `python .\.trellis\scripts\task.py create "P7.2 ChurchReport write action function migrations" --slug churchreport-write-action-function-migrations --parent 08-05-gateway-purpose-and-positioning`
- Modify/test only the exact operation and ChurchReport consumer files assigned by the P7.0 matrix

- [ ] **Step 1: Create, plan and activate the P7.2 child**

Use the fixed slug `churchreport-write-action-function-migrations`. Split sub-slices by transaction, idempotency, authorization and rollback owner; do not create generic entity or arbitrary request execution APIs.

Load `.trellis/tasks/08-05-gateway-purpose-and-positioning/p7.2-write-environment-readiness.md` and the P7.0-generated per-family fixture matrix before activation. The parent file proves only that `sunnyvalechback` is an isolated CE 9.1 environment where one test member may be created; the matrix must separately approve every required operation family's owner, allowed mutation, cleanup/reconciliation and timeout-after-commit behavior. If any required family is incomplete, pause at the P7.2 activation gate and generate a scoped operator handoff; do not return to P6/G0 and do not improvise a write target.

- [ ] **Step 2: Implement each write path test-first**

Every operation defines duplicate delivery, optimistic concurrency, partial completion, timeout-after-commit, reconciliation, sanitized errors, one authoritative writer and deterministic cleanup. No unplanned dual-write is allowed.

- [ ] **Step 3: Use the operator bridge for safe live evidence**

If D365 execution is not directly available, generate a PowerShell that creates or targets only test-owned fixtures, runs the allowlisted operation, verifies the outcome, performs cleanup/reconciliation and returns sanitized evidence. Missing non-production fixture authority is a real pause condition; mocks cannot be reported as live completion.

- [ ] **Step 4: Close P7.2**

All P7.0 matrix-assigned required write/action/function rows must be covered, evidence-linked and rollback-capable before full quality gates, task-owned commit and archive. Rows deliberately classified as forbidden generic legacy behavior are eliminated through their assigned migration/removal gate rather than exercised as live operations.

### Task 7: Complete P7.3 special-resource capabilities

**Files:**
- Create via: `python .\.trellis\scripts\task.py create "P7.3 ChurchReport special resource migrations" --slug churchreport-special-resource-migrations --parent 08-05-gateway-purpose-and-positioning`
- Modify/test exact attachment, paging, metadata and background-work files assigned by the P7.0 matrix

- [ ] **Step 1: Create, plan and activate the P7.3 child**

Use the fixed slug `churchreport-special-resource-migrations`. Separate attachment/stream, paging/large result, metadata/cache and background/scheduler ownership whenever their lifecycle or rollback owners differ.

- [ ] **Step 2: Enforce bounded ownership**

Set hard payload/page/queue/concurrency/retention limits. Each stream, buffer, continuation, cache entry, subscription, timer, task, process, handle and cancellation registration has one owner and deterministic drain/dispose behavior.

- [ ] **Step 3: Close P7.3**

Fault injection, cancellation, timeout, drain, stress/soak and retained-reference checks must return to baseline before full quality gates, task-owned commit and archive.

### Task 8: Complete P7.4 local ChurchReport cutover

**Files:**
- Create via: `python .\.trellis\scripts\task.py create "P7.4 ChurchReport ProductClient cutover" --slug churchreport-productclient-cutover --parent 08-05-gateway-purpose-and-positioning`
- Modify: ChurchReport Controller／Service／WebServiceConnector consumers identified by the final matrix
- Modify: capability-specific ChurchReport local feature-gate configuration
- Test: `ChurchReport.MemberInfo.Tests/`
- Test: `SpeechMessage.Dynamics.Tests/`

- [ ] **Step 1: Create, plan and activate the P7.4 child**

Use the fixed slug `churchreport-productclient-cutover`. Record an exact gate-to-consumer mapping and rollback command for every capability.

- [ ] **Step 2: Prove aggregate-capacity safety before the first gate**

Either all overlapping legacy/Gateway hosts use a durable shared admission authority, or the runbook proves drain-old-before-enable-new non-overlap. Per-process in-memory coordination is not aggregate evidence.

- [ ] **Step 2a: Preflight the Lenovo Dedicated Gateway listener**

Before any P7.4 browser or live capability evidence, test the deployment-owned HTTPS
listener on the Lenovo host. The current machine reserves TCP `7171-7270`, which
includes the historical development port `7244`; a direct bind therefore returns
`AccessDenied` even with no listener. If the selected port is excluded, record the
exact sanitized OS evidence and update the P7.4-owned configuration, test assertions,
launch profile and runbook as one reviewed change. Do not remove a Windows port
exclusion, silently fall back to another host, or weaken the listener test.

- [ ] **Step 3: Cut over one capability at a time on Lenovo**

Enable the smallest tier, compare behavior/evidence, observe error/latency/resource counters, then continue. Any data, authorization, error-semantic, performance or lifecycle regression rolls back only that capability; no request-time Connector/Profile/CE fallback.

- [ ] **Step 4: Close P7.4**

All ChurchReport production D365 consumers must use ProductClient with green evidence and a completed observation window before task-owned commit/archive.

### Task 9: Complete P7.5 ToolUtility／CRM SDK removal

**Files:**
- Create via: `python .\.trellis\scripts\task.py create "P7.5 ChurchReport ToolUtility removal" --slug churchreport-toolutility-removal --parent 08-05-gateway-purpose-and-positioning`
- Modify: ChurchReport project references, DI/factory wiring, legacy D365 settings and direct SDK call sites reported by the final reference scanner
- Test: `ChurchReport.MemberInfo.Tests/`
- Test: `SpeechMessage.Dynamics.Tests/`
- Test while relevant: `ToolUtility.Tests/`

- [ ] **Step 1: Create, plan and activate the P7.5 child**

Use the fixed slug `churchreport-toolutility-removal`. Freeze the last known-good rollback package before removing any reference.

- [ ] **Step 2: Remove the ChurchReport dependency surface**

Remove ChurchReport project references, DI/factory paths, legacy endpoint/credential configuration and all production use of ToolUtility, CRM SDK, `IOrganizationService`, `Entity`, `QueryBase` and `OrganizationRequest`. Do not delete the ToolUtility project if another consumer still owns it.

- [ ] **Step 3: Run the release-blocking zero-reference and lifecycle gates**

The coverage validator must have no unclassified or temporary-legacy production row. The reference scanner, Release solution build, full Dynamics/Worker/ChurchReport tests, CE matrix, p50/p95/p99, soak/drain and rollback drill must all pass.

- [ ] **Step 4: Publish the P8 handoff and close P7**

Persist contract/support-matrix versions, deployment and rollback packages, required profiles, sanitized CE evidence, monitoring/resource baselines and the zero-reference report. Perform spec update, task-owned commit and archive P7.5, then close the P7 parent only when every child evidence link is valid.

Expected final state: P6 and P7.0～P7.5 are archived; ChurchReport runs through Gateway/ProductClient successfully on Lenovo Legion; ChurchReport has no production ToolUtility／CRM SDK dependency; P8 remains uncreated or `planning` and cloud traffic is untouched.

---

## Pause and auto-recovery policy

The agent must diagnose, fix, re-run and continue automatically for compilation failures, unit-test failures, static-scan findings, formatting/encoding defects, deterministic local script defects and recoverable lifecycle bugs.

Retry budgets are finite:

- One gate receives at most three total self-repair cycles; every cycle must record a changed hypothesis or corrective action, not merely rerun the same command.
- The same root cause appearing in two consecutive cycles stops automatic repair immediately.
- External credential/profile/authorization absence receives zero blind retries and routes directly to the operator bridge.
- Transient read-only CE calls receive at most one initial attempt plus two bounded retries only when the operation contract is retry-safe. Writes are never automatically retried after ambiguous timeout unless their approved idempotency/reconciliation contract proves the outcome.

Pause for the user only when at least one condition is true:

1. A required D365/Profile/Organization fact is not derivable from repository or sanitized probe output.
2. A credential target must be created under an identity the agent cannot access; the agent must first provide a PowerShell bridge.
3. P7.2 live evidence lacks an authorized non-production/test-owned fixture or deterministic cleanup path.
4. A choice changes business semantics, authoritativeness, data retention or irreversible D365 state.
5. A destructive filesystem/Git operation outside the exact task-owned boundary would be required.
6. The gate retry budget is exhausted or the same root cause occurs twice consecutively; the checkpoint must list Task/Step, root cause, attempts, evidence and the exact user input or external-state change needed.

When pausing, persist the exact checkpoint and next command in the active Trellis task so the same Goal resumes without repeating completed phases.

## Copyable `/goal` prompt

```text
/goal

按照 Trellis Workflow，依照 `docs/superpowers/plans/2026-08-06-p6-p7-integrated-execution.md` 與
`.trellis/tasks/08-05-official-worker-router-ce-integration/scope-rebaseline-2026-08-07.md`，
先完成既有 P6.1 Router／Pool／Lease 擴充點的文件、quality、spec 判斷與正式封存；將
Official Worker CE 8.2／9.1 live compatibility 保留為 `evidence-pending` 的未來獨立支線，
不得重跑 P6.2 startup。P6 封存後，以明確路徑啟動既有
`.trellis/tasks/08-05-gateway-capability-inventory`，再依 matrix 完成並封存 P7.0～P7.5，
讓 ChurchReport 在 Lenovo Legion 支援 `Embedded + Data8` 與 `DedicatedGateway + Data8`，
並移除 ChurchReport production code／project／DI／設定對 ToolUtility、CRM SDK、
IOrganizationService、Entity、QueryBase 或 OrganizationRequest 的依賴。P6 與 P7.0 位於不同
Trellis parent，不得依賴 children traversal 尋找 P6。

以這份 Codex 計畫、對應 Trellis task artifacts 與 `.trellis/spec/` 為執行權威；`docs/superpowers/plans/2026-08-06-claude-*.md` 只作輔助參考。若 Claude 文件宣稱環境阻塞全部解除、可自由寫入或與本計畫 gate 衝突，不得採用其放寬版本。

這是一個 P6＋P7 的單一、可跨對話續跑的執行授權，不是完全無人值守承諾。我現在授權你從
P6.1 closure checkpoint 開始；你不需要在每個 Trellis phase 或 child task 再要求
「PROCEED」，但不得跳過 predecessor quality/evidence gate。技術順序固定為
`P6.1 offline closure → P7.0 → P7.1 → P7.2 → P7.3 → P7.4 → P7.5`；不得把
Official Worker live evidence 當成 Data8 主線 gate，也不得把多個 rollback owner 混成不可
分割的大 change set。

P6 closure 必須先完成 scoped Git/text baseline、離線 quality、spec 判斷與 evidence-pending 記錄，排除
`.ccg/tasks/harden-churchreport-error-recovery/.turns.json` 與任何其他 task 變更，禁止
`git add -A`。`sunnyvalechback` 是與正式系統隔離的 CE 9.1 開發 Organization、允許建立一筆
test member；這不是任意寫入授權。P7.0 matrix 產生後，P7.2 啟動前才為每個 required
operation family 定義 fixture owner、allowed mutations、cleanup/reconciliation 與
ambiguous-timeout policy；CE 8.2 write evidence 只在 matrix 標為 required 時要求。缺少某個
P7.2 fixture 時只暫停該 activation gate，不得倒退重做 P6。

P6 必須保留：它證明 deployment-owned ConnectorKind／CE version routing、Official Worker process／IPC、Router／Pool／Lease／admission、IFD credential boundary、generation drain 與資源回收；本次 P6 以離線 P6.1 完成，Official Worker live compatibility 如實記為 `evidence-pending`。兩個版本的 CE operation evidence 不在本次 P6 主線；P7.1／P7.2 才依 Data8 capability matrix 驗證 ChurchReport 業務語意。`sunnyvalechback` 有安全測試會員不會取代 operation-specific fixture gate。

Lenovo Legion 是 P6／P7 的本機開發、Gateway／Worker 執行、ChurchReport cutover 與 evidence 主機。P6 本次不得執行 CE operation 或啟動 Official Worker；只保存既有 sanitized `evidence-pending` 結果。P7 只可在 matrix 明確標為 `required`、且有核准的非正式環境／test-owned fixture 時執行 Data8 capability 所需的 read/write/action/function evidence 與 cleanup；允許逐 capability 調整 Lenovo 本機 ChurchReport feature gate 與流量。禁止猜測或保存密碼、token、cookie、connection string、private key、使用者 Session 或完整敏感 payload。

在 P7.4 第一次 browser／live evidence 前，必須先做 Lenovo listener preflight。若歷史
`https://localhost:7244` 落在 Windows excluded-port range，記錄 sanitized OS evidence，
並在同一個 P7.4-owned change 中更新 deployment config、test assertion、launch profile
與 runbook；不得刪除 OS exclusion、偷偷換 host 或放寬 listener test。

如果未來某個 P7 capability 或獨立 Official Worker task 需要 D365、本機 Credential Manager、
Windows service identity 或遠端主機步驟，先完成其餘可自動完成工作，再製作 Windows PowerShell
5.1 相容、fail-closed、只輸出去識別化結果的 task-specific handoff；本次 P6 不要求我重做
已完成的 profile／登入步驟。

若未來恢復 Official Worker task，IFD authentication 固定為大小寫敏感的 `Ifd`，identity 固定為
`WindowsCredentialReference` 加 HTTPS `homeRealm`；不要嘗試 `HostIdentity`。Worker 絕對路徑由
manifest 推導，`worker-profile.xml` 與 Gateway overlay 由既有部署工具產生，不要求操作者手工撰寫。

一般 build/test/lint/static scan/encoding/CRLF/deterministic script 問題、可修復程式錯誤與 lifecycle bug，由你自行診斷、修正、重跑並續作。只有缺少 repository 無法推導的 profile／secret target／安全 CE fixture、需要不可逆資料操作、存在真正產品語意決策，或超出 task-owned 邊界的破壞性動作時才暫停詢問我。

同一 gate 最多三次自我修復 cycle；同一 root cause 連續兩次立即停止。每次必須改變假設或修正手段，禁止無變化重跑。credential/profile/authorization 缺口零次盲目重試，直接走 operator handoff。暫停時記錄 Task/Step、根因、嘗試、證據與我必須提供的唯一下一項資料。

使用 Codex inline 模式，不派遣 implement/check subagent；不執行 Gemini／Claude 或 CCG external model runner。不得 push、建立 PR、啟動或部署 P8、操作雲端 Central Gateway，亦不得處理第二／第三產品。P8 是下一個獨立 Goal：把 ChurchReport 作為我的第一個正式產品以 `CentralGateway + Data8` 部署到雲端機房。

完成定義：P6 與 P7.0～P7.5 的 Trellis tasks 全部通過各自 quality/evidence/spec/commit/archive gate；P7 coverage matrix 無未分類或 production temporary-legacy row；每個被 matrix 標為 `required` 的 CE 8.2／9.1、Data8 connector 與 capability 組合都有真實、去識別化證據，`unsupported`／`evidence-pending` 組合明確 fail closed；ChurchReport Lenovo local flow 全部 Gateway 化；zero-reference、Release build、完整 tests、效能、stress/soak、drain/dispose、rollback drill 全綠；Session／credential／profile／tenant leakage 與 memory/resource leakage 均為零個已知或可重現 release blocker。達成後回報 P6/P7 完成摘要與 P8 獨立啟動建議，然後停止，不得自行開始 P8。
```
