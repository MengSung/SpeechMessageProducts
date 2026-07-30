# ChurchReport Local Gateway Session Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓 ChurchReport 的 Session-owned Donation 資源具備可證明的唯一 owner、lease/drain 與確定性釋放路徑，並把 Local Gateway ProductClient／preflight 納入主 DI；功能旗標在全部 gate 通過前仍維持關閉。

**Architecture:** 每一個 Session Donation cache generation 由 singleton coordinator 擁有；Scoped `InMemoryDataContextSmallGroup` 只持有 request lease。登出、重新登入、cache eviction 與 host shutdown 先讓舊 generation 不可再被租用，最後一個 request lease 歸還後才由唯一 cleanup owner Dispose Manager。Dynamics ProductClient process host 與 WhoAmI preflight 改由 ChurchReport 主 DI／host lifecycle 擁有，flag=false 嚴格 no-op。

**Tech Stack:** ASP.NET Core / .NET 10、C#、IMemoryCache、IHostedService、xUnit、FluentAssertions、System.Threading Interlocked／lease state machine。

---

## 強制共通契約

- `DynamicsAccess:Package01FeeReadsEnabled` 保持 `false`；本計畫不得更改任何實際設定值或 credential。
- 不移除 Embedded、Data8、`PowerPlatform.Dataverse.Client` 或 legacy rollback path。
- 不從 appsettings、log 或環境輸出密碼、Token、Credential、ClientId、SecretReference 實際值或 CRM 真實位址。
- 所有新增或實質修改的 Production／Test 程式，必須有完整、深入、詳細的繁體中文 XML／實作註解，說明信任邊界、唯一 owner、並行競爭、fail-closed、取消／逾時、drain／Dispose／cleanup 順序，以及效能／記憶體取捨。
- 修改檔案儲存為 UTF-8 without BOM、CRLF、final CRLF。
- 嚴格 TDD：每個 Production behavior change 前先建立 RED、執行並確認正確失敗，再做最小 GREEN。

## Task 1：Manager-owned LINE／Semaphore 資源確定性釋放

**Files:**

- Modify: `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- Create: `ChurchReport.MemberInfo.Tests/SessionLifecycle/DonationOwnedResourceLifecycleTests.cs`

- [x] **Step 1: Write RED tests**

建立反射／真實 instance 測試，證明 `DonationPaymentManager` 實作 `IDisposable`、並行重複 Dispose 只執行一次，且自己建立的 `_feeRefreshLock` 與 `LineMessagingClient` 被釋放；證明 `DonationFeePaymentProcessor.Dispose(bool)` 不再是空實作。測試不得依賴真實 LINE 或 CRM。

- [x] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~DonationOwnedResourceLifecycleTests" --logger "console;verbosity=minimal"
```

Expected: FAIL because manager does not implement Dispose and processor retains its client.

- [x] **Step 3: Implement minimal owner-safe Dispose**

`DonationPaymentManager` 使用 `Interlocked.Exchange`／`CompareExchange` 形成 concurrent-idempotent Dispose；只釋放自己建立的 `LineMessagingClient` 與 `_feeRefreshLock`，不得釋放 Factory-owned `ToolUtilityClass` 或 DI-owned workflow。先停止新進入的 manager operation，再等待 coordinator lease-drain 保證沒有 in-flight caller，最後按依賴逆序清理。`DonationFeePaymentProcessor` 修正既有空 Dispose，釋放其自建 LINE client，保留 injected workflow owner 不變。

- [x] **Step 4: Verify GREEN and format**

Run focused tests, then `dotnet format` on the three files.

## Task 2：Per-session cache generation／request lease／host drain

**Files:**

- Create: `SpeechMessageProducts.ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Startup.cs` only near the `IInMemoryDataContext` registration
- Create: `ChurchReport.MemberInfo.Tests/SessionLifecycle/SessionScopedResourceDisposalCoordinatorTests.cs`

- [x] **Step 1: Write RED state-machine tests**

測試 concurrent explicit eviction + cache callback 只 Dispose 一次、in-flight lease 在 drain 期間仍可完成、最後 lease 才 Dispose、新 acquire 不得重用 draining generation、不同 session 不得被全域鎖序列化、host shutdown 後 active entry／lease／owned resource 回到 zero。

- [x] **Step 2: Verify RED**

Expected: compilation/test failure because coordinator does not exist.

- [x] **Step 3: Implement coordinator**

使用 per-entry lock/state/ref-count，不保留 `HttpContext`、Controller、Session、Token 或 Credential。`IMemoryCache` callback 必須是 static delegate，state 只傳 singleton coordinator 或 bounded entry identity。Explicit drain 先從可見 cache 移除並標為 Draining；ref-count=0 時同步 Dispose，否則最後 lease return 是 cleanup owner。`Dispose`／host stop 先阻止新 acquire，再 drain 全部 entry，不阻塞無關 session。

- [x] **Step 4: Wire only DonationPaymentManager**

本增量只讓 `DonationPaymentManager` 使用 coordinator；其餘 11 個 legacy cache properties 保持原狀，避免擴大 scope。Scoped `InMemoryDataContextSmallGroup.Dispose` 歸還本 request 的 leases。

- [x] **Step 5: Verify GREEN**

Run lifecycle tests, existing `DonationPaymentManagerNamingTests`／`DonationPaymentServiceExtractionTests`, format and encoding gate.

## Task 3：ChurchReport 主 DI 擁有 Dynamics ProductClient 與 Local Gateway preflight

**Files:**

- Modify: `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- Create: `SpeechMessageProducts.ChurchReport/Services/DynamicsGatewayPreflightHostedService.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Startup.cs` only near existing Dynamics hosted-service registrations
- Create: `ChurchReport.MemberInfo.Tests/DynamicsGatewayPreflightHostedServiceTests.cs`

- [x] **Step 1: Write RED configuration/lifecycle tests**

測試 flag=false 時 host start no-op、不得解析 executor／不得 HTTP；flag=true + invalid Gateway options 在 host start fail-closed；WhoAmI failure／timeout 阻止 ready；outgoing request 不加入 `X-Principal`／`X-Workload` spoof headers；process host Dispose concurrent-idempotent and host-owned。

- [x] **Step 2: Verify RED**

Expected: preflight type or startup behavior missing.

- [x] **Step 3: Implement main-DI ownership**

把 process host 轉為可注入 singleton interface/implementation，移除 static mutable provider owner；flag=false 不解析 client。Preflight hosted service 只在 flag=true + Gateway mode 走正式 `IDynamicsOperationExecutor` pipeline 執行 `runtime.health.whoami`，不建立第二個 HttpClient，不送 caller identity header；任何失敗直接讓 `StartAsync` throw。

- [x] **Step 4: Verify GREEN**

Run focused preflight/bootstrap lifecycle tests and existing ProductClient/Gateway authorization tests.

## Task 4：登入／登出／重新登入共用 Session drain

**Dependencies:** Task 1 and Task 2 completed and approved.

**Files:**

- Modify: `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs`
- Modify: `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs`
- Create: `ChurchReport.MemberInfo.Tests/SessionLifecycle/AuthenticationSessionResourceDrainTests.cs`

- [x] **Step 1: Write RED integration tests**

測試 Logout 在 `Session.Clear` 前擷取舊 resource scope identity 並呼叫 drain；重新登入 fixation reset 前同樣 drain；重複 Login/Logout 不累積 live managers／LINE clients／semaphores；in-flight lease 在 logout 後可完成，完成後 baseline 歸零。

- [x] **Step 2: Verify RED**

Expected: cached manager remains visible/owned until TTL.

- [x] **Step 3: Implement minimal integration**

由 controller 注入 coordinator，不複製 cache key 或 dispose logic。Fail-closed 順序：先取舊 scope identity → request drain → 清 Session/commit/signout/delete cookies。清理不可記錄 Session ID、user ID、password、LINE ID 或 Token。

- [x] **Step 4: Verify GREEN**

Run focused auth/session tests and existing auth security suite.

## Task 5：整合驗證與審查

- [x] Run ChurchReport test project and Dynamics test project.
- [x] Run full solution Release build.
- [x] Run strict UTF-8/no-BOM/CRLF/final-CRLF gate on all added/modified files (final 23 scoped files passed).
- [x] Run scoped `dotnet format --verify-no-changes`, `git diff --check`, added-line secret scan.
- [ ] Run full Gemini + Claude review through `docs/scripts/Start-CcgDualModelRun.ps1` (Gemini passed; Claude remains quota-blocked, so only the approved degraded fallback is available).
- [x] Run Trellis spec-compliance and code-quality reviews.
- [x] Update `.trellis/spec/`, the Traditional Chinese guide, task verification, and CCG review artifacts.
- [x] Keep `Package01FeeReadsEnabled=false`; do not claim Local Gateway/CE/browser E2E complete until real evidence exists.
