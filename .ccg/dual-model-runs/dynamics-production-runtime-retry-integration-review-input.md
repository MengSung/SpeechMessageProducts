# Production Dynamics Runtime Retry Integration Test 補強審查

## 背景

前一輪完整雙模型 re-review：

```text
20260729-170800-dynamics-multi-profile-runtime-drain-recovery-reviewer
ok=true
degradedFallback=false
completedBackends=[gemini, claude]
```

兩個模型都確認原先 `slot.Draining` 永久鎖死的 Critical 已修正。Claude 留下一項 Warning：三個 Manager regression 使用 `TrackingRuntime`，沒有真正執行 Production `DynamicsProfileRuntime._drainTask` 的快取、cancellation failure 後重設與再次 drain attempt。

## 本次補強

請直接讀取：

- `SpeechMessage.Dynamics.Tests/DynamicsProfileRuntimeFactoryTests.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-multi-profile-runtime-verification.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`

新增測試：

```text
Manager_retries_the_real_runtime_after_cancelled_drain_without_allocating_a_third_generation_early
```

測試實際組合：

```text
DynamicsProfileRuntimeManager
→ RecordingRuntimeFactory（只記錄 reference，不建立或 Dispose 資源）
→ DynamicsProfileRuntimeFactory
→ DynamicsProfileRuntime
→ DynamicsHttpTransport
→ AdfsOAuthTokenProvider
→ OrganizationAdmissionRegistration
```

測試流程：

1. 真實 Runtime Generation 1 取得 Execution Lease。
2. 第一次 Replace 發布 Generation 2，Generation 1 進入 Draining。
3. caller cancellation 讓第一次 Production `DrainAttemptAsync` failure。
4. 第二次 Replace 必須重試同一個真實 Runtime；舊 Lease 釋放前 Factory CreateCount 必須維持 2。
5. Lease 釋放後才建立 Generation 3，Generation 1 成為 Disposed。
6. Manager Dispose 後 Registry EntryCount 必須回到 0。

RED 證據：刻意移除 `DynamicsProfileRuntime.DrainAttemptAsync` catch 內的 `_drainTask = null` 後，測試失敗；Manager 第二次 Replace／最終 Dispose 持續取得同一個已取消 Task，最後以 AggregateException＋TaskCanceledException 回報。

GREEN 證據：恢復 Production `_drainTask` 失敗後重設邏輯，單一整合測試通過。

最新完整驗證：

```text
Focused MultiProfile／Registry／Factory／Readiness／Phase4 Soak：36 passed
SpeechMessage.Dynamics.Tests：159 passed
SpeechMessageProducts.sln Release Build：0 warnings / 0 errors
Changed WebApi／Gateway／Tests scoped dotnet format：通過
```

## 審查問題

1. 這個測試是否真正使用 Production Factory／Runtime，而不是再次以 Fake 複製 `_drainTask` 行為？
2. Recording decorator 是否只觀測 reference，沒有形成第二個 Dispose owner、重複 cleanup 或跨測試 static state？
3. RED 證據是否能實際防止 `_drainTask` cancellation failure 永久快取的 regression？
4. 測試的 try/finally、Lease release、Manager／Registry dispose 順序是否 deterministic，無測試自己造成的 resource leak？
5. 是否已充分處理前一輪 Warning，或仍有 Critical／Warning？
6. 新增程式是否有完整、深入的繁體中文註解，說明 ownership、併發、錯誤與 cleanup 順序？

## 輸出格式

輸出 Critical／Warning／Info 分級報告。若前一輪 Warning 已充分解決且沒有新阻斷問題，明確寫 `PASS`。

