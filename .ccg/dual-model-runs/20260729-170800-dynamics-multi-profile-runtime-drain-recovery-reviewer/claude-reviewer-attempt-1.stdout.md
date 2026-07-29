# 審查報告：Dynamics Multi-Profile Runtime Drain Recovery 修正

## 審查範圍
已逐行讀取生產程式（`DynamicsProfileRuntimeManager.cs`、`DynamicsProfileRuntime.cs`、`IDynamicsProfileRuntime.cs`）與新增測試（`MultiProfileRuntimeTests.cs` 三個 RED→GREEN 案例，並比對 `DynamicsProfileRuntimeFactoryTests.cs`、`Phase4IsolationSoakTests.cs` 的 Fake 實作），逐項核對「強制審查問題」1–9。並讀取本 run 內已完成的 Gemini 審查結果（`PASS`，無 Critical/Warning）作為交叉參照。

## Critical 🔴
無。

逐項追蹤結果：
1. **已 Disposed cleanup failure**：`DrainOwnedRuntimeAsync`（`DynamicsProfileRuntimeManager.cs:578-603`）的 `finally` 只依 `runtime.State == Disposed` 且 `ReferenceEquals(slot.Draining, runtime)` 才清除引用，例外本身仍由 `try` 向外傳遞——確認例外未被吞掉，且精確清除對應物件（不會誤清另一個 Generation）。
2. **caller cancellation／timeout 未 Disposed**：production `DrainAttemptAsync`（`DynamicsProfileRuntime.cs:230-264`）在非 `TimeoutException` 的取消（如 `_shutdownCts`／caller token 取消）或未進入 `DisposeOwnedResourcesAsync` 前拋出時，`_state` 仍為 `Draining`，`DrainOwnedRuntimeAsync` 的 `finally` 因此不清除 `slot.Draining`，確認 Manager 仍持有強引用，可被下一次 `ReplaceAsync`（pendingDraining 分支）或 `DisposeCoreAsync` 最終掃描接管。
3. **Pending Draining 未完成前不得配置第三套資源**：`ReplaceCoreAsync`（`DynamicsProfileRuntimeManager.cs:476-566`）在 `pendingDraining` 重試 `await` 完成之前，不會進入遞增 `Generation`／呼叫 `_runtimeFactory.CreateAsync` 的區塊；且第二次鎖內檢查 `slot.Draining is not null` 仍會擋下配置。確認沒有第三套 Client/Token/Handler/CTS/Registration 產生的路徑。
4. **鎖與計數**：`ReplacementInProgress`、`_activeLifecycleOperations`（`BeginLifecycleOperationLocked`/`EndLifecycleOperation`）均以 `try/finally` 保證歸還，且 `EndLifecycleOperation` 的訊號釋放在鎖外進行，避免 continuation 重入鎖造成 deadlock；`DisposeCoreAsync` 等待 `lifecycleDrain` 後才清空 Catalog 並 sweep 剩餘 Runtime，確認 shutdown 期間被取消的 Replace drain 不會與最終 sweep 對同一物件產生併發雙重 Dispose（sweep 使用未連結 `_shutdownCts` 的 `default` token，且 `DrainAndDisposeAsync` 本身冪等 — 已 Disposed 直接短路、非 Disposed 失敗後 `_drainTask` 會被清空以支援重試但不會與同時進行的呼叫重疊，因為同一 Runtime 在任一時刻只有一條程式路徑持有其 owner 身分）。未發現 underflow／deadlock／double-dispose。
5. `DrainOwnedRuntimeAsync` 在 success／fault／cancellation／shutdown 四種路徑下，實際追蹤程式流程皆遵守「只依 exact-reference + State==Disposed 清除」的語意，符合契約。
6. Generation numbering、Active + 最多一個 Draining、Queue-after-admission 的 current-active 解析（`TryResolveActiveRuntimeLocked`、`AcquireAsync`）皆未被本次修正變更其原有契約。
8. 未發現本次修正引入新的 Runtime／Client／Handler／Token／CTS／Admission Registration／Task 強引用洩漏；`DisposeCoreAsync` 最終 sweep 使用獨立（非 shutdown-linked）token，確保能真正完成清理而非立即再度取消。

## Warning 🟡

- **[SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs:711-902, Phase4IsolationSoakTests.cs:618-]** 本次三個新增 regression 測試（以及既有全部 Manager 測試）都是針對手刻 Fake（`TrackingRuntime`／`Phase4IsolationSoakTests.cs` 的 `RegistryBackedRuntime`）驗證，而非針對生產類別 `DynamicsProfileRuntime.cs` 本身。
  - 具體落差：production `DrainAndDisposeAsync`（`DynamicsProfileRuntime.cs:191-208`）會快取 `_drainTask` 並在同一物件上多次呼叫時回傳同一個 in-flight Task（"多次呼叫共用同一 Task" 的明文契約），且失敗後只在 `_state != Disposed` 時才清空 `_drainTask` 讓後續呼叫重新嘗試（`DrainAttemptAsync` 的 catch，`DynamicsProfileRuntime.cs:251-263`）。而 `TrackingRuntime.DrainAndDisposeAsync`（`MultiProfileRuntimeTests.cs:861-902`）完全沒有等價的 Task 快取欄位，每次呼叫都是獨立重新執行整段邏輯。
  - `DynamicsProfileRuntimeFactoryTests.cs` 雖然使用了真正的 `DynamicsProfileRuntime`／`DynamicsProfileRuntimeFactory`，但只驗證建構與一次性 Dispose（`Crm82_and_crm91_generations_own_distinct_clients_tokens_transports_and_handlers`、`Disposing_generation_disposes_transport_token_provider_and_admission_registration_once`），未涵蓋 cancellation／timeout／cleanup-failure 後的重試路徑。
  - 影響：本次修正的核心正是「Manager 對同一個 Draining Runtime 精確重試 cleanup」，但這條路徑目前完全靠 Fake 的簡化行為驗證，`DynamicsProfileRuntimeManager` 與**真正**的 `DynamicsProfileRuntime._drainTask` 快取／重置語意之間的整合，沒有任何測試實際跑過。若未來 `DynamicsProfileRuntime` 的快取邏輯出現迴歸（例如快取沒有正確清空、或清空時機錯誤導致競態），現有測試套件不會發現。
  - 建議修正：至少新增一個把 `DynamicsProfileRuntimeManager` 與真正 `DynamicsProfileRuntimeFactory`/`DynamicsProfileRuntime`（如 `DynamicsProfileRuntimeFactoryTests.cs` 已用的建構方式）組合的整合測試，覆蓋「caller cancellation 導致未完成 drain → 下次 Replace 重試同一 Runtime → 最終成功 Dispose」與「cleanup failure 後 Generation 仍可推進」兩條路徑，讓 `_drainTask` 快取/清空邏輯被真正驗證，而不是只被 Fake 模擬。

## Info 🟢

- **[SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs:230-249]** 重試（第二次 `DrainAttemptAsync`）會重新完整等待 `_definition.DrainTimeout`，即使前一次嘗試已因逾時觸發過 `_retirementCts.Cancel()`。目前不是缺陷（`zeroTask` 若已完成會立即返回），但如果同一 Generation 反覆因暫時性 cleanup failure 被重試，觀察到的總等待時間會比單次 DrainTimeout 更長；可考慮在文件註解中明確這是預期行為，避免未來被誤判為效能迴歸。
- Gemini 本輪審查同樣給出 `PASS`，並額外對 `AdfsOAuthTokenProvider.cs` 的 `SocketsHttpHandler` 生命週期註解提出 Info 建議；該檔案屬於前一階段（`20260729-161900` 已審）異動，非本次 drain recovery 修正範圍，此處僅記錄供追蹤，不重複列為本次 finding。

## Summary
本次 `ReplaceCoreAsync` / `DrainOwnedRuntimeAsync` 修正正確解決了前次審查發現的 Critical（cleanup failure 永久卡死 Slot）：透過「exact-reference + 實際 State==Disposed」判斷清除 `slot.Draining`，同時保留未完成 Runtime 的強引用供重試，且在 Pending Draining 收斂前不遞增 Generation、不呼叫 Factory。鎖、計數器、shutdown 與 linked cancellation 的交互沒有發現 deadlock、underflow 或雙重 Dispose。唯一值得處理的是測試覆蓋落差：三個新 regression 測試與既有 Manager 測試全數建立在 Fake Runtime 上，Fake 未複製生產類別 `DynamicsProfileRuntime` 的 Task 快取／清空語意，導致這次修正最關鍵的「對同一 Runtime 精確重試」路徑，未曾與真正的生產 Runtime 類別整合測試過。建議：**批准合併**，但請補一個 Manager + 真實 `DynamicsProfileRuntimeFactory`/`DynamicsProfileRuntime` 的整合測試作為後續補強（非阻斷本次合併的必要條件）。

---
SESSION_ID: 3edcfafe-10a0-44eb-a32b-f8db4a718190
