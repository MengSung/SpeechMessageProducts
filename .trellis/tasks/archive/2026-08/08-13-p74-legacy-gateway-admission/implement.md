# P7.4 legacy Gateway admission boundary 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` for Codex inline execution. 每一項以
> TDD 推進；任何與本計畫衝突的現有 runtime 事實必須回到 `design.md`，不能自行擴大為 feature enablement。

**Goal:** 建立不持有 CRM/request state 的 ChurchReport legacy-drain control-plane、其 contract tests 與
deployment non-overlap validator/runbook，使 P7.4 能準確保持 no-go 或交給實機 owner 演練。

**Architecture:** 以 host-owned `LegacyToolUtilityDrainController` 管理受控 legacy ingress 的 stop/acquire/drain
lease lifecycle；它不當 CRM pool，也不聲稱涵蓋未註冊 legacy call。固定分類的 validator / runbook 只檢查
deployment-supplied evidence presence，不讀取或輸出秘密。

**安全審查基準：** controller 僅提供 operation-level metering。同步 ToolUtility 呼叫無法被
lease-loss cancellation fence、未註冊 legacy ingress 也不能被它觀察，且 per-host in-memory
狀態不能證明跨 host 的 aggregate capacity。因此 implementation、tests、validator 與 runbook
都必須把同步 overrun、unknown legacy coverage、non-durable topology、lease race、drain timeout
與 cleanup uncertainty 分類為 no-go；不得宣稱 `stopped-and-drained` 是 Organization-level proof。

**Tech Stack:** .NET 10、ASP.NET Core DI、xUnit、FluentAssertions、existing Dynamics ControlPlane contracts。

**停止條件：** 本 child 絕不啟用 feature flag、執行 CE mutation、切換流量、移除 ToolUtility、
建立 P7.5 或 P8。若測試發現同步呼叫在 drain deadline 後仍可能執行，保留 controller 作為
受控計量工具，但將 read-back 固定為 unknown/no-go。

---

### Task 1: 建立純記憶體、無 CRM 依賴的 drain controller contract

**Files:**

- Create: `SpeechMessageProducts.ChurchReport/Services/LegacyToolUtilityDrainController.cs`
- Create: `ChurchReport.MemberInfo.Tests/Services/LegacyToolUtilityDrainControllerTests.cs`

- [x] **Step 1: 寫失敗的 lifecycle tests**

  Tests 必須具體覆蓋：open lease increments count、`StopIntakeAsync` rejects later acquire、drain waits for
  already-issued lease、lease double-dispose does not underflow、cancelled acquire / drain does not change another
  workload’s state、timeout returns `DrainTimeout` category、shutdown leaves no active lease or waiter。

- [x] **Step 2: 執行 focused test，確認未有實作時失敗**

  Run:

  ```powershell
  dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~LegacyToolUtilityDrainControllerTests
  ```

  Expected: compile/test failure because the controller and its fixed result categories do not yet exist.

- [x] **Step 3: 實作最小 controller**

  公開 API 僅允許 server-selected `LegacyToolUtilityWorkload` enum、`CancellationToken` 與 bounded
  `TimeSpan`。以 `SemaphoreSlim` / `TaskCompletionSource` 計數，lease 以 interlocked exactly-once release，
  所有 transition 在 lock 內短暫完成。controller 不得接受或保存 CRM entity、contact ID、profile、endpoint、
  credential、delegate、exception 或 response；不啟動 thread/timer/background task。

- [x] **Step 4: 執行 focused test，確認所有 lifecycle contracts 通過**

  Run the same command. Expected: PASS with no network / CRM access.

- [x] **Step 5: 加入同步 overrun 與跨 workload isolation 測試**

  受控 lease 在 callback/operation 仍執行時不得因 drain timeout 被誤判為全域完成；A/B
  workload 的計數與取消只能影響自己的 waiter。測試輸出只能使用固定分類，不得輸出 CRM
  或個人識別資料。

### Task 2: 以 host lifecycle 掛接受控 ingress，但保持預設 disabled

**Files:**

- Modify: `SpeechMessageProducts.ChurchReport/Startup.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
- Create: `SpeechMessageProducts.ChurchReport/Services/LegacyToolUtilityAdmissionHostedService.cs`
- Test: `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
- Test: `ChurchReport.MemberInfo.Tests/Payments/DonationFeeQueryServiceAsyncTests.cs`

- [x] **Step 1: 寫 disabled-by-default tests**

  Prove default configuration does not instantiate any Gateway client, SQL coordinator, new ToolUtility client,
  background worker or outbound I/O. Prove an injected controller can be used only on the legacy fee invocation
  boundary, rejects once stopped and always releases the lease on mapping fault/cancellation.

- [x] **Step 2: 寫 minimal composition**

  Register the controller as a single ChurchReport ServiceProvider-owned singleton and the hosted service only as
  lifecycle owner. `DonationFeeQueryService` receives an optional controller; legacy branch acquires only the
  server-selected `Package01FeeRead` lease before invoking the existing synchronous method, releases it in
  `finally`, and never uses it as permission to set Package01 flag true. Default constructors retain old behavior.

- [x] **Step 3: run focused regression suites**

  ```powershell
  dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~LegacyToolUtilityDrainControllerTests|FullyQualifiedName~DonationFeeQueryServiceAsyncTests|FullyQualifiedName~DonationDynamicsAccessBootstrapLifecycleTests"
  ```

  Expected: PASS; no test emits endpoint, credential, CRM ID or personal name.

### Task 3: 交付 no-secret deployment validator 與 drain-first/non-overlap runbook

**Files:**

- Create: `docs/scripts/Test-ChurchReportLegacyGatewayNonOverlap.ps1`
- Create: `docs/runbooks/churchreport-package01-drain-first-non-overlap.md`
- Create: `ChurchReport.MemberInfo.Tests/Infrastructure/LegacyGatewayNonOverlapRunbookContractTests.cs`

- [x] **Step 1: 寫 source/contract tests**

  Assert the validator uses only bounded category inputs, requires `durable`, `canonical-binding`,
  `legacy-drained`, `legacy-coverage-proven`, `gateway-ready`, and `rollback-owner` categories, and cannot emit
  raw values. Assert runbook preserves stop → drain → read-back → readiness → one smoke → rollback ordering.

- [x] **Step 2: 實作 validator / runbook**

  The script accepts only switch / enum-like categories, returns exit 0 only when every required category is true,
  and otherwise returns a fixed no-go category. It performs no network, CRM, SQL, feature-flag or file mutation.
  The runbook assigns actual deployment read-backs to the owner and prohibits gate enablement on uncertainty.

- [x] **Step 3: 執行 validator tests and negative examples**

  Run focused tests plus the script with an incomplete category set. Expected: deterministic non-zero no-go;
  no endpoint / credential / CRM ID in output.

### Task 4: full task verification and evidence record

**Files:**

- Modify: `.trellis/tasks/08-13-p74-legacy-gateway-admission/check.jsonl`
- Modify: `.trellis/tasks/08-13-p74-legacy-gateway-admission/task.json`
- Create: `.trellis/tasks/08-13-p74-legacy-gateway-admission/check.md`

- [x] **Step 1: run complete local gates**

  ```powershell
  dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore
  dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
  git diff --check
  python .\.trellis\scripts\task.py validate 08-13-p74-legacy-gateway-admission
  ```

- [x] **Step 2: byte-level encoding and scope inspection**

  Verify every changed `.cs` / `.cshtml` is UTF-8 without BOM, CRLF-only, final CRLF and has complete Traditional
  Chinese documentation. Confirm changes do not touch the user-owned `.ccg/tasks/.../.turns.json`.

- [x] **Step 3: CCG review**

  Use `Start-CcgDualModelRun.ps1` with a 45-second maximum wait. Record both outputs, single-model fallback, or
  local-only downgrade honestly. Resolve verified Critical findings before completing this child.

## Explicit no-go / rollback points

- Any inability to account for all legacy D365 ingress remains `legacy-coverage-unproven`: do not set the feature
  gate true.
- Any blocked drain, unknown active work, cancellation, lease loss or disposal uncertainty is no-go; retain legacy
  intake state only if the controller can safely restore it, otherwise leave it stopped and hand off to deployment owner.
- No CE request, flow switch, P7.5 removal or P8 work belongs to this task.
