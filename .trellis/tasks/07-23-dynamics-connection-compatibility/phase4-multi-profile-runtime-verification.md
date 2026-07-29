# Phase 4 Multi-Profile Runtime 驗證報告

日期：2026-07-29

## 1. 本次範圍

本次增量完成 Local Gateway 與 Central Gateway 共用的 Multi-Profile Runtime 基礎與本地 deterministic 驗證，重點包含：

- 部署設定 Alias Catalog 與 `crm82`／`crm91` 隔離 Runtime Generation。
- Alias → Organization Admission Queue → 排隊完成後解析當下 Active Runtime 的固定順序。
- Active 加最多一個 Draining Generation 的 replace-and-drain 上限。
- Runtime 發布與 Gateway Ready 前的 Host Slot 前置驗證。
- Admission Permit 與 Runtime Execution Lease 的組合 ownership 與確定性釋放。
- 全部初始 Profile 成功後才原子發布 Catalog；部分初始化失敗時全部 rollback。
- Replace drain failure 依 Runtime 實際狀態復原：已 Disposed 會清除精確 Catalog reference，未完成 Draining 則由下一個 owner 在 Factory 前重試。
- Manager Shutdown 會取消發布後仍在等待的 Replace lifecycle owner，再由最終 Dispose owner 接管仍有 Lease 的 Runtime。
- Multi-Profile DI 不註冊全域可變 Client、Transport 或 Token Provider。
- `/ready` 彙整所有 Active Profile，只輸出 Alias、Generation、狀態與 bounded Admission 指標。
- 所有新 Production／Test Runtime、Routing、Admission 與 Lifecycle 程式補上具體繁體中文 XML／實作註解。

這不是整個 Phase 4 完成證明。真實 Local Gateway、D365 CE 8.2／9.1、跨 Process capacity、Fault／Soak／Performance 與 Consumer Migration 仍是後續 Gate。

## 2. 資料流與 ownership 契約

```text
Operation Request
  → 已核准 Alias
  → Canonical Organization Admission Queue
  → Admission Permit
  → 排隊完成後解析當下 Active Runtime
  → Runtime Execution Lease
  → Generation-owned Client／Transport／Token Provider
```

Queue wait 只可保留 bounded `DispatchEnvelope`、Admission Manager 與不可變 Plan，不得保存 Runtime、Client、Handler、Token Provider、Credential、User、LINE ID、JWT、Session 或 Generation reference。

排隊完成後取得 Runtime Lease 時，Manager 會重新驗證：

- Active Runtime 仍可接受新 Lease。
- Admission Manager 物件身分與排隊前相同。
- Canonical Organization Key 相同。
- Configuration Digest 相同。

組合租約的釋放順序固定為：

```text
Runtime Execution Lease
→ Organization Admission Permit
```

兩個 cleanup 都必須被嘗試並等待。第一步失敗不能阻止第二步歸還 Organization capacity。

## 3. RED→GREEN 錯誤路徑證據

### 3.1 Runtime acquisition rollback

新增測試：

```text
Acquisition_rollback_releases_permit_when_runtime_lease_cleanup_fails
```

RED 證據：

- Runtime 已增加 active reference 並建立 Lease。
- 取得流程接著拋出原始 acquisition failure。
- Runtime Lease Dispose 也拋出 cleanup failure。
- 舊實作直接離開 catch，回報的只有 cleanup failure，Admission Permit 沒有被釋放。

GREEN 契約：

- 原始 acquisition failure 保留為第一個錯誤。
- Runtime Lease cleanup failure 一起 Aggregate 回報。
- Runtime active execution count 回到零。
- Admission active permits 回到零。

### 3.2 Initialization cleanup failure 與同步 Task 發布競態

新增測試：

```text
Initialization_cleanup_failure_preserves_original_error_and_allows_retry
```

RED 證據：

- `crm82` 候選 Runtime 建立成功。
- `crm91` Factory 注入原始初始化失敗。
- `crm82` Runtime cleanup 也注入失敗。
- 舊實作讓 cleanup failure 中斷狀態回滾，原始 Factory failure 被遮蔽。
- 初步修正後，測試又揭露同步完成競態：Core catch 先把 `_initializationTask` 清成 null，外層賦值隨後把舊失敗 Task 寫回，第二次 Initialize 仍取得第一次錯誤。

GREEN 契約：

- 初始化核心先建立明確非同步邊界，確保 Manager 已發布本次 Task ownership。
- 所有候選 Runtime 都會嘗試清理。
- 原始 Factory failure 與 cleanup failure 一起回報。
- `_ready` 保持 false，Snapshot 不保留候選 Runtime。
- `_initializationTask` 重設，第二次 Initialize 建立新 Generation 並成功 Ready。

### 3.3 Disposed cleanup failure 不可形成幽靈 Draining reference

新增測試：

```text
Disposed_draining_cleanup_failure_is_reported_and_does_not_block_later_replacement
```

RED 證據：

- Generation 2 已原子發布，Generation 1 已完成 Disposed 狀態與 cleanup 嘗試。
- cleanup 結尾注入 failure 後，舊 Manager 因 await 拋錯而跳過 `slot.Draining = null`。
- 快照同時保留已 Disposed Generation 1 與 Active Generation 2，後續 Replace 永久被入口檢查拒絕。

GREEN 契約：

- cleanup failure 仍原樣向上回報，不吞錯、不假裝成功。
- Manager 只在 Runtime 已是 Disposed、且 Slot 仍指向同一物件時移除強引用。
- 快照只保留 Active Generation 2，後續 Replace 可建立 Generation 3。

### 3.4 未完成 Draining 必須先重試，Factory 前禁止第三套資源

新增測試：

```text
Unfinished_draining_runtime_is_retried_before_allocating_the_next_generation
```

RED 證據：

- Generation 1 尚有 Execution Lease，Generation 2 已發布後，caller cancellation 中止第一次 drain。
- Generation 1 正確保持 Draining，但舊 `ReplaceAsync` 只要看到 `slot.Draining != null` 就永久拒絕後續 Replace。

GREEN 契約：

- `ReplacementInProgress` 仍保證同 Alias 同時只有一個非同步 lifecycle owner。
- 前次 owner 已離開、只剩 Draining 時，下一個 Replace 先重試同一 Runtime cleanup。
- 舊 Lease 釋放前 Factory CreateCount 維持 2；Generation 不遞增，也不配置第三套 Client／Token／Handler graph。
- Generation 1 cleanup 完成後才建立 Generation 3，再安全 drain Generation 2。

### 3.5 Manager Shutdown 必須取消 Replace owner，但保留最終 cleanup ownership

新增測試：

```text
Manager_shutdown_cancels_the_published_replacement_drain_owner
```

RED 證據：發布後 drain 若只使用 caller token，Manager Shutdown 已進入 NotReady 仍無法中止 Replace wait，測試在兩秒觀察窗得到 TimeoutException。

GREEN 契約：

- 發布後 drain 使用 caller 與 `_shutdownCts` 的 linked token。
- Replace owner 立即觀察 `OperationCanceledException` 並結束 lifecycle count。
- Manager Dispose 仍保持未完成 Runtime 的唯一 ownership，直到測試歸還舊 Lease 才逐一 Dispose，沒有 use-after-dispose 或孤兒背景 Task。

### 3.6 Production Runtime `_drainTask` cancellation recovery 整合證據

雙模型 re-review 指出原三個 Manager regression 使用 Tracking Runtime，未真正執行 Production
`DynamicsProfileRuntime._drainTask` 快取與失敗後重設語意。新增測試：

```text
Manager_retries_the_real_runtime_after_cancelled_drain_without_allocating_a_third_generation_early
```

測試組合真正的：

```text
DynamicsProfileRuntimeManager
→ recording decorator
→ DynamicsProfileRuntimeFactory
→ DynamicsProfileRuntime
→ DynamicsHttpTransport／AdfsOAuthTokenProvider／OrganizationAdmissionRegistration
```

RED 證據：刻意移除 Production `DrainAttemptAsync` catch 內的 `_drainTask = null` 後，第一次 caller cancellation 會讓 Runtime 永久快取已取消 Task；第二次 Replace 與 Manager 最終 Dispose 都只能再次取得相同 cancellation failure，測試以 Aggregate／TaskCanceledException 失敗。

GREEN 契約：

- 第一次取消保留 Runtime `Draining` state 與 Manager Catalog ownership。
- faulted/cancelled `_drainTask` 被清成 null，第二次 Replace 可建立新的 drain attempt。
- 舊 Lease 釋放前 Production Factory CreateCount 維持 2；之後才建立 Generation 3。
- 最終 Registry EntryCount 回到零，證明 Transport、Token Provider 與 Admission Registration ownership 已完整釋放。

## 4. 測試與建置結果

### 4.1 全部 Dynamics Tests

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --no-restore `
  --logger "console;verbosity=minimal"
```

結果：

```text
Passed 159
Failed 0
Skipped 0
```

### 4.2 Multi-Profile／Phase 4 focused suite

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --no-restore `
  --filter "FullyQualifiedName~MultiProfileRuntimeTests|FullyQualifiedName~OrganizationAdmissionRegistryTests|FullyQualifiedName~DynamicsProfileRuntimeFactoryTests|FullyQualifiedName~GatewayReadinessTests|FullyQualifiedName~Phase4IsolationSoakTests" `
  --logger "console;verbosity=minimal"
```

結果：

```text
Passed 36
Failed 0
Skipped 0
```

### 4.3 Solution Release Build

```powershell
dotnet build .\SpeechMessageProducts.sln `
  --configuration Release `
  --no-restore `
  --verbosity minimal
```

結果：

```text
0 warnings
0 errors
```

### 4.4 NuGet vulnerability audit

```powershell
dotnet list .\PowerPlatform.Dataverse.Client\PowerPlatform.Dataverse.Client.csproj `
  package --vulnerable --include-transitive
```

結果：目前套件來源未回報已知易受攻擊套件。

此結果只代表 NuGet 已知弱點稽核通過，不代表 Data8 source 已成為永久推薦依賴，也不取代 CE 8.2 real-server、WCF lifecycle、Socket／Handle soak 或 replacement gate。

## 5. 格式、註解與編碼驗證

- WebApi、Gateway、Tests 的 changed `.cs` 已使用 scoped `dotnet format --verify-no-changes --no-restore --include ...` 驗證，全部通過。
- 新增 Runtime Manager、Runtime、Factory、Admission Registry、Profile Routed Executor 與測試 Fake 的 `<inheritdoc />` 已改為實質繁體中文 XML 文件。
- 註解說明責任、信任邊界、併發、唯一 owner、失敗結果、rollback 與 Drain／Dispose 順序，不只翻譯語法。
- Changed text files 以 strict UTF-8 decoder、BOM、CRLF、replacement character 與 `git diff --check` 作為交付 Gate。

## 6. SPEC 回饋

`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 已新增 `Multi-Profile Runtime Admission, Publication, and Rollback` 可執行情境，固定以下規則：

- Queue 不得持有 Runtime 強引用。
- Host Slot 是 Runtime 發布／Ready 前置條件。
- Initialization 必須在同步失敗路徑前完成 Task ownership 發布。
- Rollback 即使前一步 cleanup 失敗仍要繼續釋放其他 ownership。
- 原始操作錯誤不能被 cleanup failure 覆蓋。
- 需要 acquisition cleanup failure 與 initialization retry regression tests。
- Drain reference 必須依 terminal state 與 exact reference identity 清除，不能以「await 有沒有成功」代替生命週期狀態。
- 未完成 Draining 由後續唯一 replacement owner 在 Factory 前重試；已 Disposed cleanup failure 則回報錯誤後清除幽靈 reference。
- 發布後 drain 必須使用 Manager shutdown linked cancellation，最後 cleanup ownership 由 Manager Dispose 接管。

## 7. 雙模型審查與收斂結果

本增量共執行三次正式 self-healing CCG run：

1. `20260729-161900-dynamics-multi-profile-runtime-reviewer`
   - Gemini PASS。
   - Claude 找到一項有效 Critical：Drain cleanup failure 會永久保留 `slot.Draining`。
2. `20260729-170800-dynamics-multi-profile-runtime-drain-recovery-reviewer`
   - Gemini PASS。
   - Claude 確認 Critical 已修正，但提出一項有效 Warning：Manager regression 未執行 Production `_drainTask` 語意。
3. `20260729-172452-dynamics-production-runtime-retry-integration-reviewer`
   - Gemini PASS，無 Critical／Warning。
   - Claude PASS，無 Critical／Warning。
   - `ok=true`、`degradedFallback=false`、`quotaBlocked=false`。

Gemini 最後僅提出 UTF-8 with BOM 的 Info 建議；此建議與使用者要求及 Repository `.editorconfig` 的 UTF-8 without BOM＋CRLF 契約衝突，因此不採用，並由 strict encoding gate 持續驗證。

## 8. 尚未完成的發布 Gate

1. 真實 Local Gateway 與 ChurchReport localhost ProductClient 串接。
2. Central Gateway 多產品部署與跨 Process durable coordinator 驗證。
3. WinRM、DC／D365 VM 與瀏覽器 E2E。
4. CE 8.2 與 CE 9.1 real-server authentication、WhoAmI、CRUD、Query／FetchXML、Paging、Action／Function／Batch 矩陣。
5. Data8 Legacy Worker 或官方替代 Worker 的 deterministic lifecycle、restart、rollback 與長時間 Socket／Handle／Memory baseline。
6. Fault injection、跨 Host aggregate capacity、Soak 與 Performance SLO。
7. Phase 5 ChurchReport 第一個可回滾 Consumer Migration。
8. Phase 6 Data8、CRM SDK、ProjectReference、Solution Entry 與 buildable source removal。

因此本報告只證明本次 Multi-Profile Runtime 本地增量通過目前 deterministic test／build gate，不宣告整體任務、Phase 4 或 SDK removal 已完成。
