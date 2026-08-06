# [CLAUDE] P6＋P7 Lenovo Legion 本機執行計畫

> **檔案標記：CLAUDE 系列第 2 份。** 由 Claude 撰寫。
> 總контракт在 `2026-08-06-claude-churchreport-master-plan.md`，**先讀它**。
> 不要修改非 CLAUDE 檔案（`2026-08-06-p6-p7-integrated-execution.md` 等由 Codex 擁有）。
>
> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans`（inline 模式）。
> Steps 用 checkbox（`- [ ]`）追蹤。不派遣 implement／check subagent。

**Goal:** 在 Lenovo Legion 完成 P6 Official Worker／CE 整合與 P7.0～P7.5 ChurchReport 全量 Gateway 化，
移除產品端 ToolUtility／CRM SDK 依賴，並產出 P8 雲端部署可直接接手的 immutable handoff。

**Architecture:** 持續執行的 umbrella plan。代理依序管理既有 P6 與 P7.0，
再依 capability matrix 建立 P7.1～P7.5 children；每個 child 各自完成
Trellis planning → activation → TDD implementation → quality gate → spec update →
task-owned commit → archive，前一 gate 全綠後自動銜接下一個。

**Tech Stack:** .NET 10／ASP.NET Core、net48 Official CRM 8.2／9.1 Workers、Data8 Connector、
Trellis task workflow、Windows PowerShell 5.1、xUnit、Visual Studio 2026。

## Global Constraints

繼承 master plan 第 4 節全部內容，特別是：

- 硬性不變量 6 條（4.3）
- 文字檔 UTF-8 no BOM／CRLF-only／final CRLF／無行尾空白（4.4）
- Operator PowerShell bridge（4.5）
- 重試預算 3 次／同根因 2 次（4.6）
- 每個新增或實質修改的 C# lifecycle／concurrency type 必須有完整繁體中文文件，
  說明 trust boundary、唯一 owner、timeout／cancellation、drain／dispose 與 isolation

---

### Task 1: 保護既有 P6 checkpoint 並確認起點

**Files:**
- Read: `.trellis/tasks/08-05-official-worker-router-ce-integration/prd.md`
- Read: `.trellis/tasks/08-05-official-worker-router-ce-integration/design.md`
- Read: `.trellis/tasks/08-05-official-worker-router-ce-integration/implement.md`
- Read: `.trellis/tasks/08-05-official-worker-router-ce-integration/p6.2-ce-readiness-evidence.md`
- Preserve: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.ps1`
- Preserve: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1`

- [ ] **Step 1: 載入 Trellis 與 task 脈絡**

```powershell
python .\.trellis\scripts\get_context.py
git status --short
```

Expected：current task 是 `official-worker-router-ce-integration`、狀態 `in_progress`，
readiness probe 變更仍在。

- [ ] **Step 2: 重跑 readiness probe 的 focused tests**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Test-DynamicsOfficialWorkerDeploymentReadiness.Tests.ps1
```

Expected：`All official Worker deployment readiness probe tests passed.`

- [ ] **Step 3: 確認 P6.1 仍綠，且不重寫它**

跑 P6 focused test set、完整 Dynamics tests 與 Release build。
有 regression 就在 P6-owned 檔案內修；**不因為 P6.2 未完成就丟棄既有 P6.1 證據**。

- [ ] **Step 4: 確認基線乾淨**

```powershell
git status --short
git diff --check
```

Expected：`git diff --check` 無輸出；無與 P6／P7 無關的未提交產品程式。
若有，先單獨 commit 或隔離，**不得**與後續 task-owned commit 混在一起。

### Task 2: 讓 P6.2 readiness 收斂為 Go

**Files:**
- Use: `docs/scripts/Publish-DynamicsOfficialWorkers.ps1`
- Use: `docs/scripts/Test-DynamicsOfficialWorkerDeploymentReadiness.ps1`
- Use: `docs/scripts/New-DynamicsOfficialWorkerDeployment.ps1`
- Use: `docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.ps1`
- Create 於 repository 外: `%LOCALAPPDATA%\SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json`
- Create 部署輸出: `artifacts/dynamics-workers-p6.2/dynamics-official-workers.gateway.json`
- Create 部署輸出: `artifacts/dynamics-workers-p6.2/crm82/worker-profile.xml`
- Create 部署輸出: `artifacts/dynamics-workers-p6.2/crm91/worker-profile.xml`
- Update: `.trellis/tasks/08-05-official-worker-router-ce-integration/p6.2-ce-readiness-evidence.md`

> `artifacts/` 已被 `.gitignore` 忽略，部署材料不進 repository。

- [ ] **Step 1: 蒐集非機密 profile 事實**

對 CE 8.2 與 CE 9.1 取得：ProfileAlias、WorkerKind、package-lock ID、generation ID、
HTTPS organization base URI、organization name、expected OrganizationId、
`authentication`、以及 credential reference **目標名稱**。

**已知強制值（master plan §3.1）：**

- `authentication` = `"Ifd"`（字面、大小寫敏感）
- `identity.mode` = `"WindowsCredentialReference"`（`HostIdentity` 在 IFD 下必然失敗）
- `identity` 必須**恰好**含 `mode`、`reference`、`homeRealm` 三個屬性
- `homeRealm` 必須是 HTTPS、非 placeholder

密碼本身留在 Windows Credential Manager，**永不進入 JSON、命令列、log 或 artifact**。

repository 無法推導的欄位，用 master plan §4.5 的 operator bridge 取得。

- [ ] **Step 2: 產出固定版本 Worker 與 manifest**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Publish-DynamicsOfficialWorkers.ps1 -OutputRoot .\artifacts\dynamics-workers-p6.2
```

Expected：`artifacts\dynamics-workers-p6.2\official-worker-manifest.json` 產生；
crm82／crm91 兩組 executable 雜湊有效。

- [ ] **Step 3: 驗證身分與 credential target 存在**

在預計執行 Gateway／Worker 的身分下執行。
`-ExpectedExecutionIdentity` 用動態取值，避免硬編碼：

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

Expected：`outcome` = `go`；輸出不含 identity、endpoint、OrganizationId 或 credential reference 值。

**常見失敗與對應：**

| reason code | 原因 | 處理 |
|---|---|---|
| `identity-shape-invalid` | IFD 誤用 `HostIdentity`，或 `identity` 屬性數量不符 | 改回三屬性的 `WindowsCredentialReference` |
| `identity-value-invalid` | `homeRealm` 非 HTTPS，或 `reference` 含非法字元 | 修正 profile input |
| `credential-reference-unresolvable` | credential 建在別的使用者帳號下 | 用執行 Worker 的同一帳號重建（master plan §3.2） |
| `profile-value-invalid` | placeholder GUID、非 HTTPS base URI、workerKind 不符 | 補正確值 |
| `execution-identity-mismatch` | 目前身分 ≠ `-ExpectedExecutionIdentity` | 換帳號執行或修正參數 |

- [ ] **Step 4: 原子化產出部署材料**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\New-DynamicsOfficialWorkerDeployment.ps1" `
  -ManifestPath "$root\artifacts\dynamics-workers-p6.2\official-worker-manifest.json" `
  -ProfileInputPath $profileInput `
  -OutputDirectory "$root\artifacts\dynamics-workers-p6.2" `
  -Json
```

Expected：`outcome` = `provisioned`、`featureGateMustRemainDisabled` 為 true、既有目標未被覆寫。
若目標已存在，**唯讀檢視**並走文件化的 rollback／cleanup 路徑；
**不得**刪除未確認的路徑或覆寫部署材料。

- [ ] **Step 5: 離線驗證身分鏈**

對 crm82 與 crm91 分別執行（`-ValidateOnly` 與 `-EnableLiveCompatibility` 互斥）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-DynamicsOfficialWorkerCompatibility.ps1" -ValidateOnly -Json
```

Expected：manifest、overlay、worker profile、package lock、executable hash、generation、
worker kind 全部一致；**不啟動任何 Gateway 或 Worker 行程**。

### Task 3: P6.2 受控 CE read-only 證據並封存 P6

**Files:**
- Update: `.trellis/tasks/08-05-official-worker-router-ce-integration/p6.2-ce-readiness-evidence.md`
- Update（若有可重用契約）: `.trellis/spec/backend/`
- Archive: `.trellis/tasks/08-05-official-worker-router-ce-integration/`

- [ ] **Step 1: 只啟動已核准的本機 Gateway／Worker composition**

ChurchReport 的 feature flag 與流量**保持不變**。
先驗 health／ready；**`/ready` 本身不是 CE 證據**。

- [ ] **Step 2: 依風險順序執行 allowlisted matrix**

1. CE 9.1 Data8 `runtime.health.whoami`（control measurement，若已設定）
2. Official CRM 8.2／9.1 `runtime.health.whoami`
3. Official CRM 8.2／9.1 `runtime.pool.validate.connection`
4. 最後才執行**一筆**資料最小化範圍明確的 bounded read

**P6 禁止** write、Action、Function、generic CRUD、任意 FetchXML 與 ChurchReport consumer cutover。

- [ ] **Step 3: Drain 並證明資源回到基線**

取得成功、失敗、取消、逾時樣本後，停止新 admission、drain leases、
關閉 IPC 與 Worker 行程，然後確認 permit、slot、process、pipe、stream、timer、task、
registration、handle 計數回到宣告基線。

**任一項無法回到基線 = release blocker。**

- [ ] **Step 4: 失敗處理**

任一 CE／IPC／resource leak／incorrect result 失敗即停止後續 operation、
drain Official generation、保存 sanitized evidence、維持 P6 `in_progress`。
成功證據**不得**外推到另一個 CE version、profile、operation 或 package lock。
**不得**自動改用另一個 Connector、Profile、CE version 或 transport。

- [ ] **Step 5: P6 品質與結案 gate**

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --nologo
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo
git diff --check
```

全綠後執行 byte-level UTF-8／CRLF 檢查、Trellis check、spec-update 判斷、
**只含 task-owned 變更**的 commit，然後 archive P6。

Expected：P6 已封存；P7.0 仍為 `planning`，等下一個 Task 明確啟動。

### Task 4: 啟動並完成 P7.0 inventory／coverage gate

**Files:**
- Start: `.trellis/tasks/08-05-gateway-capability-inventory/`
- Read: `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json`
- Update: `.trellis/tasks/08-05-gateway-capability-inventory/preliminary-capability-inventory.json`
- Create: `docs/scripts/Test-DynamicsCapabilityCoverage.ps1`
- Create: `docs/scripts/Test-DynamicsCapabilityCoverage.Tests.ps1`
- Update: `.trellis/tasks/08-05-gateway-capability-inventory/{prd,design,implement}.md`

**Interfaces:**
- Consumes: P6 已封存狀態；Phase 0 的 70 筆 normalized call-site rows
- Produces: green coverage matrix 與 P7.1～P7.3 的可執行工作佇列

- [ ] **Step 1: 啟動既有 task**

```powershell
python .\.trellis\scripts\task.py start .\.trellis\tasks\08-05-gateway-capability-inventory
```

Expected：P7.0 變 `in_progress`；尚無 P7.1～P7.5 child。

- [ ] **Step 2: 更新 source-derived manifest**

重算 70-row source hash，掃描 Registry、Data8 executor、Official Worker protocol／Router、
ProductClient、consumer gates 與 ChurchReport ToolUtility／CRM SDK references。

**分開保存** declared／implemented／enabled／real-evidence 四種狀態，
禁止合併成單一「完成」欄位。**不把 70 rows 等同 70 operations。**

- [ ] **Step 3: 測試先行實作 deterministic validator**

測試必須證明：固定排序、byte-stable JSON、穩定 rule ID、失敗時非零 exit code、
bounded input、無網路存取，以及拒絕下列情形——
未分類 row、缺 owner／DTO、重複或不合規 ID、Registry-only、未知 connector／CE、
generic CRUD／FetchXML、缺 lifecycle ownership、無 owner 的 legacy dependency。

validator **不得**碰觸 D365、credential、token、cookie、connection string 或真實產品設定。

- [ ] **Step 4: 產出 P7.1～P7.3 可執行工作佇列**

每個 capability family 指定：canonical phase、精確 source／test 檔案 ownership、
DTO／ProductClient／Registry／executor owner、CE support target、rollout／rollback owner、evidence gate。

**transaction 或 rollback owner 不同的 family，即使共用同一 entity 也要拆成不同 child。**

- [ ] **Step 5: 產出 ToolUtility／CRM SDK reference-scan 基線**

P7.0 只**報告**現有數量；P7.5 才把 zero count 變成必須通過的 release assertion。
不留長期 red CI test。

- [ ] **Step 6: 封存 P7.0**

validator tests、JSON 解析、encoding／CRLF 檢查、`git diff --check` 全綠後，
Trellis check、spec 判斷、只含 P7.0-owned 變更的 commit、archive。

### Task 5: P7.1 Read capabilities

**Files:**
- Create task: `.trellis/tasks/<date>-churchreport-read-capability-migrations/`（slug 固定）
- Modify by matrix ownership: `SpeechMessage.Dynamics.Abstractions/Operations/`
- Modify by matrix ownership: `SpeechMessage.Dynamics.Connectors.Data8/`
- Modify by matrix ownership: `SpeechMessage.Dynamics.WorkerHost/` 與版本化 Worker adapters
- Modify by matrix ownership: `SpeechMessage.Dynamics.ProductClient/`
- Modify by matrix ownership: P7.0 matrix 指定的 ChurchReport read consumers
- Test: `SpeechMessage.Dynamics.Tests/`、`SpeechMessage.Dynamics.Crm82Worker.Tests/`、
  `SpeechMessage.Dynamics.Crm91Worker.Tests/`、`ChurchReport.MemberInfo.Tests/`

- [ ] **Step 1: 建立、規劃並啟動 P7.1 child**

用固定 slug `churchreport-read-capability-migrations`，
由 P7.0 佇列寫出完整 PRD／design／implement，然後 `task.py start`。

- [ ] **Step 2: 以 Package01 交付模板 vertical slice**

先寫 fail-first 的 contract／authorization／connector-support／lifecycle tests；
再實作 bounded DTO、catalog／Registry、executor、ProductClient 與 feature gate；
取得 CE evidence 與 legacy parity 之後才啟用本機 consumer。

每個 slice 固定包含：

1. fail-first contract／authorization／support-matrix tests
2. bounded request／response DTO 與 stable Operation ID
3. Registry、executor support 與 ProductClient
4. cancellation、timeout、paging、error sanitization、permit／lease cleanup
5. legacy parity 或 bounded shadow comparison
6. capability feature gate、rollout owner、rollback owner
7. CE 8.2／9.1 evidence 與效能／資源基線

- [ ] **Step 3: 依模板完成其餘 read family**

一次一個 rollback-owned slice。
**Shadow read 共用 bounded deadline，且永不改變 authoritative response**；
所有 response buffer、task、timer、cancellation registration、permit、lease 都要釋放。

- [ ] **Step 4: 封存 P7.1**

validator、focused tests、完整 Dynamics／Worker／ChurchReport tests、Release build、
parity、p50／p95／p99、soak／lifecycle、encoding、diff gate 全綠後才 commit／archive。

### Task 6: P7.2 Write／Action／Function capabilities

**Files:**
- Create task: `.trellis/tasks/<date>-churchreport-write-action-function-migrations/`
- Modify／test: 僅限 P7.0 matrix 指派的 operation 與 ChurchReport consumer 檔案

> **環境已就緒：** 寫入驗證使用 `sunnyvalechback` Organization。
> 使用者已確認可自由寫入、寫壞可刪、不影響正式系統（master plan §3 第 4 項）。
> 因此本 Task **不因缺少 fixture 而暫停**，但仍必須有 cleanup／reconciliation。

- [ ] **Step 1: 建立、規劃並啟動 P7.2 child**

固定 slug `churchreport-write-action-function-migrations`。
依 **transaction、idempotency、authorization、rollback owner** 拆 sub-slice，
**不依** CRM entity 或 ToolUtility 方法拆。
**不得**建立 generic entity 或任意 request execution API。

- [ ] **Step 2: 每條寫入路徑測試先行**

每個 operation 必須定義：duplicate delivery、optimistic concurrency、partial completion、
timeout-after-commit、reconciliation、sanitized errors、
**唯一一條 authoritative writer**、確定的 cleanup。

**禁止未經協議的 dual-write。**

- [ ] **Step 3: 對 `sunnyvalechback` 取得 live write evidence**

只針對 test-owned fixture 執行 allowlisted operation，驗證結果，
執行 cleanup／reconciliation，輸出去識別化 evidence。

若無法直接執行，依 master plan §4.5 產生 operator bridge script。
**mock 不得回報為 live completion。**

- [ ] **Step 4: 封存 P7.2**

所有 write／action／function row 都必須已涵蓋、有 evidence 連結、可回滾，
才進入完整品質 gate、task-owned commit 與 archive。

### Task 7: P7.3 Special-resource capabilities

**Files:**
- Create task: `.trellis/tasks/<date>-churchreport-special-resource-migrations/`
- Modify／test: P7.0 matrix 指派的 attachment、paging、metadata、background-work 檔案

- [ ] **Step 1: 建立、規劃並啟動 P7.3 child**

固定 slug `churchreport-special-resource-migrations`。
當 lifecycle 或 rollback owner 不同時，attachment／stream、paging／large result、
metadata／cache、background／scheduler 必須分開 ownership。

- [ ] **Step 2: 強制有界所有權**

- Attachment／stream：大小、類型、buffer、timeout、dispose 硬上限
- Paging／large result：continuation token、page size、retention、cancellation 有界，
  不保留跨 user／session state
- Background／scheduler：queue、retry、idempotency、shutdown drain、
  subscription／timer／task owner 可驗證
- Metadata cache：Profile／Organization 隔離、容量／TTL 上限、eviction／dispose 路徑明確

每個 stream、buffer、continuation、cache entry、subscription、timer、task、process、
handle、cancellation registration 都**恰有一個 owner** 與確定的 drain／dispose 行為。

- [ ] **Step 3: 封存 P7.3**

fault injection、cancellation、timeout、drain、stress／soak 與 retained-reference 檢查
都回到基線後，才進入完整品質 gate、commit、archive。

### Task 8: P7.4 本機 ChurchReport cutover

**Files:**
- Create task: `.trellis/tasks/<date>-churchreport-productclient-cutover/`
- Modify: 最終 matrix 指定的 ChurchReport Controller／Service／WebServiceConnector consumers
- Modify: capability 專屬的 ChurchReport 本機 feature-gate 設定
- Test: `ChurchReport.MemberInfo.Tests/`、`SpeechMessage.Dynamics.Tests/`

- [ ] **Step 1: 建立、規劃並啟動 P7.4 child**

固定 slug `churchreport-productclient-cutover`。
記錄每個 capability 精確的 gate-to-consumer 對應與 rollback 命令。

- [ ] **Step 2: 第一個 gate 之前先證明 aggregate-capacity 安全**

二擇一：

- 所有重疊的 legacy／Gateway host 共用 **durable shared admission authority**；或
- runbook 證明 **drain-old-before-enable-new** 的非重疊順序

**每個 process 各自的 in-memory coordinator 不構成 aggregate 證據**
（`InMemoryRuntimeHostSlotCoordinator` 自述 `IsDurable=false`）。

- [ ] **Step 3: 在 Lenovo 上逐 capability 切換**

啟用最小 tier → 比對行為／證據 → 觀察 error／latency／resource 計數 → 繼續下一個。
任何資料、授權、錯誤語意、效能或 lifecycle 退步，**只回滾該 capability**。
**不在 request-time 做 Connector／Profile／CE fallback。**

- [ ] **Step 4: 封存 P7.4**

所有 ChurchReport production D365 consumer 都改用 ProductClient、證據全綠、
觀測窗完成後，才 task-owned commit／archive。

### Task 9: P7.5 移除 ToolUtility／CRM SDK 並產出 P8 handoff

**Files:**
- Create task: `.trellis/tasks/<date>-churchreport-toolutility-removal/`
- Modify: ChurchReport project references、DI／factory wiring、legacy D365 settings、
  reference scanner 回報的直接 SDK call sites
- Create: `.trellis/tasks/08-05-gateway-purpose-and-positioning/p8-handoff.md`
- Test: `ChurchReport.MemberInfo.Tests/`、`SpeechMessage.Dynamics.Tests/`、
  `ToolUtility.Tests/`（仍相關時）

- [ ] **Step 1: 建立、規劃並啟動 P7.5 child**

固定 slug `churchreport-toolutility-removal`。
**移除任何 reference 之前，先凍結最後一個 known-good rollback package。**

- [ ] **Step 2: 移除 ChurchReport 的依賴面**

移除 project references、DI／factory 路徑、legacy endpoint／credential 設定，
以及所有 production 使用的 ToolUtility、CRM SDK、`IOrganizationService`、
`Entity`、`QueryBase`、`OrganizationRequest`。

> **若 repository 仍有其他 consumer 擁有 ToolUtility，不得刪除 ToolUtility project。**
> P7.5 只代表 ChurchReport 不再依賴它；ToolUtility 的最終退役是獨立 task。

- [ ] **Step 3: 執行 release-blocking gate**

- coverage validator 無未分類、無 production temporary-legacy row
- reference scanner zero count
- Release solution build
- 完整 Dynamics／Worker／ChurchReport tests
- CE matrix、p50／p95／p99
- soak／drain
- **rollback drill 實際演練**

- [ ] **Step 4: 產出 P8 immutable handoff**

寫入 `p8-handoff.md`：

1. 凍結的 contract／support-matrix 版本
2. 可重現的 deployment 與 rollback package
3. P8 必需的 required profiles 清單
4. 去識別化 CE 8.2／9.1 evidence
5. 監控／資源基線
6. zero-reference report
7. **明確標示 P8 需獨立 Goal 與授權**

- [ ] **Step 5: 封存 P7.5 與 P7 parent**

spec update、task-owned commit、archive P7.5；
**每個 child 的 evidence 連結都有效**時，才關閉 P7 parent。

---

## 最終期望狀態

- P6 與 P7.0～P7.5 全部封存
- ChurchReport 在 Lenovo Legion 透過 Gateway／ProductClient 正常執行
- ChurchReport 無 production ToolUtility／CRM SDK 依賴
- P8 仍未建立或維持 `planning`，雲端流量未被觸碰
- `p8-handoff.md` 已產出

**本計畫到此結束。接下來是 CLAUDE 系列第 3 份文件，需要使用者另外授權。**
