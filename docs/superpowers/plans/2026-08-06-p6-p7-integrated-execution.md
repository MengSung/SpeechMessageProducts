# P6＋P7 Continuous Trellis Execution Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` in Codex inline mode. Steps use checkbox (`- [ ]`) syntax for tracking. Do not dispatch implement/check sub-agents.

**Goal:** 以一次可續跑的 `/goal` 授權，先完成前置 G0 P6 feasibility gate，再從目前 P6.2 狀態連續完成並封存 P6，自動完成 P7.0～P7.5，使 ChurchReport 在 Lenovo Legion 全量改走 Gateway／ProductClient 並移除 ChurchReport 的 ToolUtility／CRM SDK production dependency。

**Architecture:** 這是一個 operator-assisted、可跨對話續跑的 umbrella plan，不是一個不可驗證的巨型 change set，也不是完全無人值守批次。代理在同一 Goal 內先收斂 P6 profile／credential readiness，並記錄 P7.2 的 CE 9.1 環境級可行性；P7.0 產生 capability matrix 後，才為每個必要 write／Action／Function family 建立 fixture owner、cleanup 與 reconciliation gate。接著代理依序管理既有 P6 與 P7.0，再依 matrix 建立 P7.1～P7.5 children；每個 child 各自完成 Trellis planning、activation、TDD implementation、quality gate、spec update、task-owned commit 與 archive。P8 是把第一個正式產品 ChurchReport 部署到雲端 Central Gateway 的獨立後續 Goal，本計畫不得啟動 P8。

**Tech Stack:** .NET 10／ASP.NET Core、net48 Official CRM 8.2／9.1 Workers、Data8 Connector、Trellis task workflow、PowerShell 5.1 deployment/readiness tools、xUnit、Visual Studio 2026、Lenovo Legion local development environment。

**Feasibility verdict:** **Conditional Go**。這個 Goal 可以在目前 P6 readiness=`no-go` 時直接啟動，因為 G0 會先產生一次整合 operator handoff；但 P6 不可能由代理單獨越過缺少的 CE Organization／IFD 與同身分 credential target。Worker 絕對路徑由 manifest 推導，`worker-profile.xml` 與 Gateway overlay 由既有部署工具產生，不要求使用者手工撰寫。`sunnyvalechback` 只證明 CE 9.1 有隔離的開發環境與 test-member 可行性，不代表 P7.2 所有 operation family 已獲任意寫入授權。

**Authority:** 本文件與其引用的 Trellis task artifacts／`.trellis/spec/` 是 P6／P7 執行主軸。`2026-08-06-claude-*.md` 只作輔助稽核；若其「環境阻塞全部解除」、「可自由寫入」或其他敘述與本文件衝突，不得用來放寬 gate。

---

## P6／P7 responsibility split

| 階段 | 必須證明的契約 | `sunnyvalechback` 的影響 |
|---|---|---|
| P6 | deployment-owned ConnectorKind／CE version routing、Official Worker process／IPC、Router／Pool／Lease／admission、IFD credential boundary、generation drain 與資源回收；真機只做 allowlisted read-only evidence | 提供 CE 9.1 read-only control target；不取消 P6，也不授權業務寫入 |
| P7.0 | 從 70-row source matrix 產生 capability／support／evidence matrix，逐項決定 CE 8.2／9.1 是 `required`、`unsupported` 或 `evidence-pending` | 將它記為 CE 9.1 safe-write candidate environment，但不預先假設每個 capability 都可安全測試 |
| P7.2 | 驗證 ChurchReport 的 write／Action／Function 業務語意、idempotency、authoritative writer、timeout-after-commit、fixture ownership、cleanup 與 reconciliation | 只可操作 matrix 核准且有 test-owned fixture 的 operation family；「可新增一筆測試會員」不得擴張成任意 financial／appointment／destructive writes |

因此 P6 與 P7.2 不是重複工作。P6 是 connector／version／lifecycle 底座；P7.2 才是產品業務寫入契約。

## Continuous-execution contract

使用者的一次 Goal 授權包含：

- 從既有 `.trellis/tasks/08-05-official-worker-router-ce-integration` 的 P6.2 續作；不重開 P5、不重做已綠的 P6.1。
- 在 predecessor gate 全綠時建立、規劃並啟動 P7.1～P7.5 child tasks，不需逐階段再次要求「PROCEED」。
- 允許修改 P6／P7 所需產品程式、測試、task artifacts 與本機開發設定；允許 Lenovo 上 capability-level feature gate 與 ChurchReport local traffic cutover。
- 允許對明確配置的 CE 8.2／9.1 執行 P6 read-only evidence；P7 write/action/function 的 live evidence 只限明確核准的非正式環境或 test-owned fixture，且必須有 cleanup／reconciliation。
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

此 bridge 適用於 P6.2 CE profile/evidence、P7 CE fixture evidence 與 D365 server-side inspection。一般編譯、測試與本機檔案工作不得要求使用者代做。

---

### Task 0: Complete the pre-go feasibility gate (G0)

**Files:**
- Create: `.trellis/tasks/08-05-official-worker-router-ce-integration/p6-p7-execution-baseline.md`
- Create: `.trellis/tasks/08-05-official-worker-router-ce-integration/operator-handoff-p6.2.md`
- Read/Update: `.trellis/tasks/08-05-gateway-purpose-and-positioning/p7.2-write-environment-readiness.md`
- Validate: `docs/superpowers/plans/2026-08-06-p6-p7-integrated-execution.md`
- Validate: `docs/superpowers/plans/2026-08-06-p6-p8-roadmap-rebaseline.md`
- Validate: all rebaseline Trellis Markdown／JSON files listed in the companion roadmap plan

- [ ] **Step 1: Establish a clean, scoped Git baseline**

Run `git status --porcelain=v2 --untracked-files=all`, `git diff --cached --name-only` and `git diff --name-only`. Record every pre-existing dirty path with owner and disposition. Do not stash, discard, stage or clean another task's file automatically.

Current 2026-08-06 evidence shows the unrelated dirty file `.ccg/tasks/harden-churchreport-error-recovery/.turns.json` plus this rebaseline's planning documents, but no unrelated ChurchReport product source file in `git status`. This snapshot is advisory only: the Goal must re-enumerate the worktree instead of trusting it. If any unrelated `.cs`、`.cshtml`、`.csproj`、product config or migration file is dirty, G0 is No-Go until it has an independent commit/worktree or the user selects an isolation action.

After text/JSON validation passes, create scoped baseline commits using explicit path allowlists: planning/rebaseline documents are separate from P6 readiness scripts, and `.ccg/tasks/harden-churchreport-error-recovery/.turns.json` is excluded. Never use `git add -A` or stage an unreviewed path. Record the baseline commit IDs in `p6-p7-execution-baseline.md` before product implementation begins.

- [ ] **Step 2: Complete the text-format baseline**

Normalize every modified planning/task text file to strict UTF-8 without BOM, CRLF-only and final CRLF. Remove trailing whitespace, including Markdown hard-break spaces that make `git diff --check` fail. Parse all changed JSON and run `git diff --check`; G0 remains No-Go on any failure.

- [ ] **Step 3: Front-load P6 operator-owned materials**

Known facts are Lenovo CE 8.2/9.1 reachability, IFD authentication and the local execution identity `LENOVO-LEGION\Administrator`. IFD forces `authentication: "Ifd"` plus `identity.mode: "WindowsCredentialReference"`, an HTTPS `homeRealm`, and a Credential Manager target visible to the same Windows user that runs Worker.

The operator must supply or confirm only the facts the repository cannot safely invent: CE 8.2/9.1 organization base URI/name/OrganizationId, IFD home realm, credential-target name and credential presence under the execution identity. The agent derives absolute Worker executable paths from the manifest and generates `worker-profile.xml`／Gateway overlay through `New-DynamicsOfficialWorkerDeployment.ps1`.

Create a bounded PowerShell handoff for the operator-owned facts. G0 becomes P6-ready only after the profile input passes the readiness probe as `go`; `profile-input-required` is an expected pause, not a retryable software error.

- [ ] **Step 4: Record P7.2 environment-level feasibility without over-authorizing writes**

The user has confirmed `sunnyvalechback` is a CE 9.1 IFD company-development Organization isolated from the formal system, and that one test member may be created without affecting formal data. Record that exact environment-level authority in the P7 parent so it survives P6 and P7.0 archival. Do not pre-create records and do not claim operation-family readiness before P7.0 produces the final matrix.

CE 8.2 write/action/function evidence is required only for a capability whose P7.0 support matrix explicitly marks CE 8.2 as required for ChurchReport or another approved workload. An unsupported combination must fail closed and be documented; the absence of a CE 8.2 write sandbox must not unconditionally block the CE 9.1 ChurchReport first-product roadmap.

After P7.0 identifies the required operation families, each P7.2 slice must separately name its fixture owner, allowed mutations, precondition, bounded cleanup/reconciliation and ambiguous-timeout policy. If one family lacks a safe fixture, only that family remains No-Go; P6, P7.0, P7.1 and unrelated slices may continue, but P7.5 cannot claim complete coverage until every required combination is evidenced or explicitly unsupported by the approved matrix.

- [ ] **Step 5: Declare G0 outcome**

G0 is `go` for P6 execution only when Git/text baselines are green and P6 readiness is `go`. The P7.2 environment-level fact is recorded during G0 to avoid rediscovery, but it is not a substitute for the later capability-specific P7.2 activation gate and cannot block P6. If the user starts the Goal before P6 materials exist, the agent performs Tasks 0.1～0.4, emits one consolidated P6 operator handoff, persists the checkpoint and pauses; after the user returns sanitized output, the same Goal resumes without repeating completed checks.

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

Expected: current task is `official-worker-router-ce-integration`, status is `in_progress`, the committed readiness-probe scripts and current Worker artifact generation exist, and Task 0 recorded a green scoped baseline.

- [ ] **Step 2: Re-run the focused readiness-probe tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1
```

Expected: `All official Worker deployment readiness probe tests passed.`

- [ ] **Step 3: Confirm P6.1 remains green without rewriting it**

Run the P6 focused test set from the task `implement.md`, then the full Dynamics tests and Release build. Any regression is fixed in P6-owned files before proceeding; existing P6.1 evidence is not discarded merely because P6.2 is incomplete.

### Task 2: Make P6.2 Lenovo deployment readiness Go

**Files:**
- Read: `artifacts/dynamics-workers-p6.2/official-worker-manifest.json`
- Use conditionally: `docs/scripts/Publish-DynamicsOfficialWorkers.ps1`
- Test: `docs/scripts/Publish-DynamicsOfficialWorkers.Tests.ps1`
- Use: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.ps1`
- Use: `docs/scripts/New-DynamicsOfficialWorkerDeployment.ps1`
- Create outside repository: `%LOCALAPPDATA%\SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json`
- Create local Gateway publish: `artifacts/dynamics-workers-p6.2/gateway-host/`
- Create deployment output: `artifacts/dynamics-workers-p6.2/gateway-host/dynamics-official-workers.gateway.json`
- Create deployment outputs: `artifacts/dynamics-workers-p6.2/crm82/worker-profile.xml`
- Create deployment outputs: `artifacts/dynamics-workers-p6.2/crm91/worker-profile.xml`
- Update: `.trellis/tasks/08-05-official-worker-router-ce-integration/p6.2-ce-readiness-evidence.md`

- [ ] **Step 1: Reuse the current immutable Worker generation when valid**

Run the publish-script focused tests, parse the existing manifest and independently verify both executable hashes. Reuse the current `artifacts/dynamics-workers-p6.2` generation when its manifest, package locks, worker kinds and executable hashes are consistent. Only publish to a new clean/versioned output directory when the generation is absent or hash-invalid; do not overwrite an existing artifact tree merely to rerun a command.

- [ ] **Step 2: Collect only non-secret profile facts**

For CE 8.2 and CE 9.1, use the G0-approved ProfileAlias, WorkerKind, package-lock ID, generation ID, HTTPS organization base URI, organization name, expected OrganizationId, IFD home realm and credential-reference target. Authentication is fixed to case-sensitive `Ifd`; identity mode is fixed to `WindowsCredentialReference`. Credential values remain in Windows Credential Manager／approved secret provider and never enter the JSON.

If repository evidence cannot provide a field, use the G0 PowerShell operator bridge under the intended Gateway／Worker Windows identity. Do not retry `HostIdentity`: it is invalid for IFD. Absolute Worker paths and generated XML/overlay are agent/tool-owned outputs, not user inputs.

- [ ] **Step 3: Validate identity and credential-target presence**

Run under the intended Lenovo Gateway／Worker identity:

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$profileInput = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json'

powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Test-DynamicsOfficialWorkerDeploymentReadiness.ps1" `
  -ManifestPath "$root\artifacts\dynamics-workers-p6.2\official-worker-manifest.json" `
  -ProfileInputPath $profileInput `
  -ExpectedExecutionIdentity $identity `
  -Json
```

Expected: `outcome` is `go`; output contains no identity, endpoint, OrganizationId or credential-reference value.

- [ ] **Step 4: Publish one clean local Gateway host and generate deployment files atomically**

The overlay must be adjacent to the Gateway executable that will actually run. If `artifacts/dynamics-workers-p6.2/gateway-host` does not exist, publish the reviewed Gateway there once:

```powershell
$gatewayHost = "$root\artifacts\dynamics-workers-p6.2\gateway-host"
dotnet publish "$root\SpeechMessage.Dynamics.Gateway\SpeechMessage.Dynamics.Gateway.csproj" `
  --configuration Release --no-restore --nologo --output $gatewayHost
```

Expected: the clean directory contains the Release Gateway executable/content and no deployment overlay yet. If the directory already exists, inspect its executable, configuration and overlay read-only; do not republish or overwrite an unresolved host generation.

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\New-DynamicsOfficialWorkerDeployment.ps1" `
  -ManifestPath "$root\artifacts\dynamics-workers-p6.2\official-worker-manifest.json" `
  -ProfileInputPath $profileInput `
  -OutputDirectory $gatewayHost `
  -Json
```

Expected: `outcome` is `provisioned`, `featureGateMustRemainDisabled` is true, the overlay is adjacent to the actual Gateway executable, each `worker-profile.xml` is adjacent to its Worker executable, and no existing target is overwritten. If a target already exists, inspect it read-only and use the documented rollback/cleanup path; never delete an unresolved path or overwrite deployment material.

- [ ] **Step 5: Validate the generated identity chain without network access**

Use the exact deployed paths and run `Invoke-DynamicsOfficialWorkerCompatibility.ps1 -ValidateOnly` separately for crm82 and crm91:

```powershell
$manifestPath = "$root\artifacts\dynamics-workers-p6.2\official-worker-manifest.json"
$overlayPath = "$gatewayHost\dynamics-official-workers.gateway.json"
$gatewayEndpoint = 'https://localhost:7244/'

powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-DynamicsOfficialWorkerCompatibility.ps1" `
  -ManifestPath $manifestPath -GatewayOverlayPath $overlayPath `
  -GatewayEndpoint $gatewayEndpoint -ProfileAlias crm82 `
  -ExpectedWorkerKind OfficialCrm82Worker -ValidateOnly -Json

powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-DynamicsOfficialWorkerCompatibility.ps1" `
  -ManifestPath $manifestPath -GatewayOverlayPath $overlayPath `
  -GatewayEndpoint $gatewayEndpoint -ProfileAlias crm91 `
  -ExpectedWorkerKind OfficialCrm91Worker -ValidateOnly -Json
```

Expected: manifest, overlay, worker profile, package lock, executable hash, generation and worker kind agree; no Gateway or Worker process is started.

### Task 3: Execute P6.2 read-only CE evidence and close P6

**Files:**
- Use for WhoAmI: `docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.ps1`
- Create when absent: `docs/scripts/Invoke-DynamicsOfficialWorkerP6Evidence.ps1`
- Create when absent: `docs/scripts/Invoke-DynamicsOfficialWorkerP6Evidence.Tests.ps1`
- Update: `.trellis/tasks/08-05-official-worker-router-ce-integration/p6.2-ce-readiness-evidence.md`
- Update when reusable contract exists: `.trellis/spec/backend/`
- Archive: `.trellis/tasks/08-05-official-worker-router-ce-integration/`

- [ ] **Step 1: Start only the approved local Gateway／Worker composition**

Keep ChurchReport feature flags and traffic unchanged. Validate health／ready first; `/ready` alone is not CE evidence.

- [ ] **Step 2: Close the current live-harness capability gap test-first**

`Invoke-DynamicsOfficialWorkerCompatibility.ps1` is intentionally hard-coded to `runtime.health.whoami`; do not claim it can execute the rest of P6.2. If no equivalent bounded tool exists, create `Invoke-DynamicsOfficialWorkerP6Evidence.ps1` plus Windows PowerShell 5.1 tests. The tool accepts only `runtime.pool.validate.connection` and, when an outside-repository approved input file is explicitly supplied, `fee.dedication.retrieve.by.contact.date.range`; it must reject arbitrary operation IDs／FetchXML／entity names, never echo parameters or secrets, own and dispose all HTTP/stream/CTS/buffer resources, and require a separate live opt-in switch. Tests must prove allowlist enforcement, bounded input/output, no-live dry run, sanitized failure and deterministic cleanup.

- [ ] **Step 3: Run the required allowlisted matrix in risk order**

Use the isolated `sunnyvalechback` CE 9.1 Organization for the Data8 WhoAmI control and Official CRM 9.1 `runtime.health.whoami`／`runtime.pool.validate.connection`. Run the equivalent Official Worker identity/connection evidence against the separately approved CE 8.2 profile. These two Official operations for both versions are the required P6.2 connector/version matrix. A fee read may run only when the operator supplies an approved test-owned contact/date-range input outside the repository; if that input is absent, defer the business read/parity evidence to P7.1 rather than keeping P6 open. Do not run write, Action, Function, generic CRUD or arbitrary FetchXML in P6; the available CE 9.1 test member belongs to P7.2, not P6.

- [ ] **Step 4: Drain and prove resource return without injecting live CE faults**

After the approved successful read-only matrix, stop new admission, drain leases, close IPC and Worker processes, then confirm permit, slot, process, pipe, stream, timer, task, registration and handle counts return to the declared baseline. Failure／cancellation／timeout fault-injection evidence belongs to P6.1's deterministic local harness and may be re-run offline if stale; P6.2 must not deliberately create ambiguous live CE failures merely to duplicate that evidence.

- [ ] **Step 5: Run P6 quality and closure gates**

Run focused P6 tests, full Dynamics/Worker tests, ChurchReport MemberInfo tests, Release solution build, byte-level UTF-8/CRLF checks and `git diff --check`. Perform Trellis spec-update judgment, create a task-owned commit and archive P6. Do not include unrelated user changes.

Expected: P6 is archived and P7.0 is still `planning` until the next step explicitly starts it under the integrated Goal authorization.

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

按照 Trellis Workflow，依照 `docs/superpowers/plans/2026-08-06-p6-p7-integrated-execution.md`，先完成 Task 0 的 G0 feasibility gate，再從目前既有 task `.trellis/tasks/08-05-official-worker-router-ce-integration` 的 P6.2 checkpoint 開始，連續完成並正式封存 P6；之後直接以明確路徑啟動 `.trellis/tasks/08-05-gateway-capability-inventory`，再自動完成與封存 P7.0～P7.5，直到 ChurchReport 在 Lenovo Legion 全部透過 Gateway／ProductClient 正確執行，且 ChurchReport production code／project／DI／設定不再依賴 ToolUtility、CRM SDK、IOrganizationService、Entity、QueryBase 或 OrganizationRequest。P6 與 P7.0 位於不同 Trellis parent，不得依賴 children traversal 尋找 P6。

以這份 Codex 計畫、對應 Trellis task artifacts 與 `.trellis/spec/` 為執行權威；`docs/superpowers/plans/2026-08-06-claude-*.md` 只作輔助參考。若 Claude 文件宣稱環境阻塞全部解除、可自由寫入或與本計畫 gate 衝突，不得採用其放寬版本。

這是一個 P6＋P7 的單一、可跨 operator handoff 續跑的執行授權，不是完全無人值守承諾。我現在授權你從 P6 readiness=`no-go` 的現況開始處理；`no-go` 代表先完成 G0／operator handoff，不代表這個 Goal 無效。你不需要在每個 Trellis phase 或 child task 再向我要求 PROCEED：只要當前 predecessor quality/evidence gate 全綠，且新 child 的 `prd.md`、`design.md`、`implement.md` 與 Phase 1.4 activation readiness 已通過，你可以建立、規劃、執行 `task.py start`、實作、Trellis check、spec update、建立只含 task-owned 變更的本機 commit、archive，然後自動進入下一個 child。技術順序固定為 G0 → P6.2 → P6 結案 → P7.0 → P7.1 → P7.2 → P7.3 → P7.4 → P7.5；不得跳過 predecessor gate，也不得把多個 rollback owner 混成不可分割的大 change set。

G0 必須先完成兩個 P6 gate：第一，建立乾淨且 scoped 的 Git/text baseline，排除 `.ccg/tasks/harden-churchreport-error-recovery/.turns.json` 與任何其他 task 變更，禁止 `git add -A`；第二，使 P6 readiness 由 `profile-input-required` 收斂為 `go`。同時把 `sunnyvalechback` 是與正式系統隔離的 CE 9.1 開發 Organization、允許建立一筆 test member 的事實記錄到 `.trellis/tasks/08-05-gateway-purpose-and-positioning/p7.2-write-environment-readiness.md`，但不要把它解讀成任意寫入授權。P7.0 matrix 產生後，P7.2 啟動前才為每個 required operation family 定義 fixture owner、allowed mutations、cleanup/reconciliation 與 ambiguous-timeout policy；CE 8.2 write evidence 只在 matrix 標為 required 時要求。缺少 P6 必要項時先提供一次整合 PowerShell/operator handoff 並保存 checkpoint；缺少某個 P7.2 fixture 時只暫停該 activation gate，不得假裝已 Go，也不得倒退重做 P6。

P6 必須保留：它證明 deployment-owned ConnectorKind／CE 8.2／9.1 version routing、Official Worker process／IPC、Router／Pool／Lease／admission、IFD credential boundary、generation drain 與資源回收；P6 真機只做 allowlisted read-only evidence。兩個 version 的 `runtime.health.whoami` 與 `runtime.pool.validate.connection` 是必要矩陣；fee read 只有在我提供 repository 外、test-owned contact/date-range input 時才執行，否則移至 P7.1，不阻塞 P6。P7.2 才驗證 ChurchReport write／Action／Function 的產品業務語意。`sunnyvalechback` 有安全測試會員不會取代 P6。

Lenovo Legion 是 P6／P7 的本機開發、Gateway／Worker 執行、ChurchReport cutover 與 evidence 主機。允許 P6 對已核准 CE 8.2／9.1 profile 執行 allowlisted read-only evidence；允許 P7 在明確核准的非正式環境／test-owned fixture 執行完成 capability 所必需的 read/write/action/function evidence 與 cleanup；允許逐 capability 調整 Lenovo 本機 ChurchReport feature gate 與流量。禁止猜測或保存密碼、token、cookie、connection string、private key、使用者 Session 或完整敏感 payload。

如果你無法直接執行某個 D365、本機 Credential Manager、Windows service identity 或遠端主機步驟，先完成其餘可自動完成工作，再製作一支 Windows PowerShell 5.1 相容、fail-closed、只輸出去識別化結果的 task-specific script 與手把手 operator-handoff 文件，明確告訴我要在哪一臺機器、使用哪個 Windows 帳號、逐步如何執行。我貼回 sanitized 結果後，你要從保存的 checkpoint 自動續跑，不要把整個工作交回給我。

P6 使用 IFD，因此 authentication 固定為大小寫敏感的 `Ifd`，identity 固定為 `WindowsCredentialReference` 加 HTTPS `homeRealm`；不要嘗試 `HostIdentity`。Organization／IFD 與 Credential Manager target 需要我或目標環境協助；Worker 絕對路徑由 manifest 推導，`worker-profile.xml` 與 Gateway overlay 由既有部署工具產生，不要要求我手工撰寫。

一般 build/test/lint/static scan/encoding/CRLF/deterministic script 問題、可修復程式錯誤與 lifecycle bug，由你自行診斷、修正、重跑並續作。只有缺少 repository 無法推導的 profile／secret target／安全 CE fixture、需要不可逆資料操作、存在真正產品語意決策，或超出 task-owned 邊界的破壞性動作時才暫停詢問我。

同一 gate 最多三次自我修復 cycle；同一 root cause 連續兩次立即停止。每次必須改變假設或修正手段，禁止無變化重跑。credential/profile/authorization 缺口零次盲目重試，直接走 operator handoff。暫停時記錄 Task/Step、根因、嘗試、證據與我必須提供的唯一下一項資料。

使用 Codex inline 模式，不派遣 implement/check subagent；依我既有要求，不執行 Gemini／Claude 或 CCG external model runner。不得 push、建立 PR、啟動或部署 P8、操作雲端 Central Gateway，亦不得處理第二／第三產品。P8 是下一個獨立 Goal：把 ChurchReport 作為我的第一個正式產品部署到雲端機房並透過 Central Gateway 跑通。

完成定義：P6 與 P7.0～P7.5 的 Trellis tasks 全部通過各自 quality/evidence/spec/commit/archive gate；P7 coverage matrix 無未分類或 production temporary-legacy row；所有必要 CE 8.2／9.1 組合有真實、去識別化證據；ChurchReport Lenovo local flow 全部 Gateway 化；zero-reference、Release build、完整 tests、效能、stress/soak、drain/dispose、rollback drill 全綠；Session／credential／profile／tenant leakage 與 memory/resource leakage 均為零個已知或可重現 release blocker。達成後回報 P6/P7 完成摘要與 P8 獨立啟動建議，然後停止，不得自行開始 P8。
```
