# Dynamics Multi-Profile Runtime 審查報告

**審查範圍**：目前 worktree 的未提交變更（Multi-Profile Runtime Manager / Factory / Registry / Token Provider / Gateway DI 與 readiness）。已完整讀取核心生命週期實作（`DynamicsProfileRuntimeManager`、`DynamicsProfileRuntime`、`DynamicsProfileRuntimeFactory`、`OrganizationAdmissionRegistry`、`AdfsOAuthTokenProvider`、`ControlledOperationExecutor`、DI 註冊、`Program.cs`、`CapacityKeys`/`OrganizationAdmissionPlan`）與對應測試（`MultiProfileRuntimeTests`、`OrganizationAdmissionRegistryTests`、`DynamicsProfileRuntimeFactoryTests`、`Phase4IsolationSoakTests`、`WebApiServiceCollectionExtensionsTests`、`AdfsOAuthTokenProviderTests`）。三項回歸修復（AcquireAsync rollback、InitializeCoreAsync 回滾、`Task.Yield()` 競態修正）均已對照程式碼與對應測試逐行核實，實作與敘述一致。

---

## Critical 🔴

**`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs:509-527`（`ReplaceCoreAsync`）— Drain 失敗會讓 Alias 永久卡在 Draining，阻斷後續所有 Replace 並洩漏 Catalog 強引用**

- **問題**：新 Generation 發布後（`published = true`，第 501-507 行），程式呼叫 `await previous.DrainAndDisposeAsync(callerCancellationToken)`（第 509 行）。只有在**這次呼叫成功回傳**時，第 511-517 行才會把 `slot.Draining` 清為 `null`。但外層 `catch` 區塊（519-527 行）只在 `!published` 時清理 `candidate`，**完全沒有處理 `published == true` 但 `DrainAndDisposeAsync` 拋例外的情況**。`finally`（528-536 行）也只重設 `ReplacementInProgress`，不會碰 `slot.Draining`。
- **`DrainAndDisposeAsync` 確實會在正常運作下拋例外**（依 `DynamicsProfileRuntime.cs:230-264` 的 `DrainAttemptAsync`）：
  1. 自然 drain 超時 + retirement 後的 `CancellationGracePeriod` 也超時（第二次 `WaitAsync` timeout）→ 拋 `TimeoutException`，此時 Runtime **仍未** 進入 `Disposed`（資源仍在使用中，這是刻意設計，見 `DynamicsProfileRuntime.cs:226-229` 註解）。
  2. `DisposeOwnedResourcesAsync()`（`DynamicsProfileRuntime.cs:285-319`）彙整 Token/Transport/Admission Registration 清理失敗並拋 `AggregateException`——但此時 `_state` 已在第 300 行提前設為 `Disposed`，也就是清理**已嘗試**、Runtime **已真正 Disposed**，只是回報了一個例外。例如 `OrganizationAdmissionRegistry.ReleaseAsync` 內部 `managerToDispose.DisposeAsync()`（若底層 SQL Host Slot Coordinator 在 drain 當下短暫不可用）就可能觸發這條路徑。
- **後果**：一旦命中，`slot.Draining` 永遠指向 `previous`（`DynamicsProfileRuntimeManager.cs:127-131` 的 `ReplaceAsync` 守衛 `if (slot.ReplacementInProgress || slot.Draining is not null) throw ...` 會對該 Alias 的**所有**後續 `ReplaceAsync` 呼叫永久拋 `InvalidOperationException`），且沒有任何背景機制會重試或清除它——只有整個 Manager `DisposeAsync()`（Gateway 完全關閉）才會透過 `EnumerateOwnedRuntimes`/`DisposeRuntimesAsync` 把它收掉。這代表：
  - 該 CRM Alias（crm82 或 crm91）從此**無法再做設定熱替換**，除非重啟整個 Gateway process。
  - 即使 `previous` 已經真正 `Disposed`（案例 2），Catalog 仍持續強引用一個已回收的 Runtime 物件，違反本次 PR 反覆強調的「確定性回收、不留下強引用」不變量，也會讓 `GetSnapshot()`/`/ready` 持續回報一個幽靈 Draining Generation（`DynamicsProfileRuntimeManager.cs:737-747` 的 `EnumerateOwnedRuntimes` 對 `slot.Draining` 不做狀態篩選）。
- **驗證缺口**：`MultiProfileRuntimeTests.cs` 中最接近的測試 `Drain_timeout_cancels_bounded_old_generation_work`（166-185 行）與 `Rapid_third_replacement_is_rejected_without_allocating_a_generation`（137-159 行）都只涵蓋「drain 最終成功」的情境，從未讓 `DrainAndDisposeAsync` 真的拋例外並斷言後續 `ReplaceAsync` 是否仍可用——因此 155 通過的測試並未覆蓋此路徑。規劃文件（`docs/superpowers/plans/2026-07-29-dynamics-multi-profile-runtime.md:438-440`）本身也只描述成功路徑（「under the lock, clear the exact old draining reference」），未定義失敗語意，這正是實作留下的落地缺口。
- **建議最小修正**：在 `catch`（或改寫成 `finally` 中依 `previous.State` 判斷）內，只要 `ReferenceEquals(slot.Draining, previous)` 且 `previous.State == DynamicsProfileRuntimeState.Disposed`（代表清理已完成、只是回報例外），就在 `lock (_gate)` 內清除 `slot.Draining = null`，讓下一次 `ReplaceAsync` 能繼續。若 `previous.State` 仍非 `Disposed`（真正卡住的 Lease），至少需要一個可觀察、可重試的路徑（例如允許呼叫端對同一 Alias 重新觸發 drain，而不是無限期 fail-closed），並在文件/規格中明確定義這個語意，而不是讓它成為無法逆轉的靜默狀態。

---

## Warning 🟡

無其餘可獨立成立、附可重現失敗路徑的 Warning 級發現。以下面向已個別驗證且未發現缺陷，僅供記錄：

- `DynamicsProfileRuntimeManager.AcquireAsync`（184-273 行）：Runtime Lease 取得後拋例外、且 Lease 自身 Dispose 也失敗的 rollback 順序（249-271 行）已正確保留原始例外與 cleanup 例外，並經 `Acquisition_rollback_releases_permit_when_runtime_lease_cleanup_fails` 測試以「先建立 ownership 再失敗」的忠實 Fake 驗證，非套套邏輯測試。
- `InitializeCoreAsync`（341-428 行）：候選清理失敗與 Factory 失敗並存時，`_ready`/`_initializationTask` 仍會在同一鎖內無條件重設（399-411 行），允許重試；`Task.Yield()`（346 行）確實避免了同步完成 Factory 造成 `_initializationTask` 被舊失敗 Task 覆蓋回去的競態。
- `AdfsOAuthTokenProvider` 新增的 Dispose/DisposeAsync（前述 diff）：透過 `_disposeCts` 連結 caller token、`_gate` single-flight 與 `Interlocked`/`Volatile` 旗標正確避免了 Dispose 與進行中 Token 請求之間的 use-after-dispose 競態。
- `OrganizationAdmissionRegistry` 的四個反向索引（GUID／Base URI／Admission Namespace／Lease Namespace）與引用計數 Dispose 順序（鎖內短暫操作、鎖外 await Manager.DisposeAsync）設計正確，測試涵蓋完整（碰撞、compat digest、idempotent shutdown）。
- `OrganizationAdmissionPlan` 把 `MaxConnectionsPerServer` 移出 `ConfigurationDigest`（讓 crm82／crm91 共用同一實體 Organization 容量但允許不同 Socket 上限）邏輯正確，且已在 `TryCreate` 中驗證其不超過 `LocalMaxInFlight`，測試 `Different_profile_connection_limits_keep_one_shared_capacity_digest` 直接驗證此不變量。
- DI 註冊（`AddSpeechMessageDynamicsProfiles`）刻意不註冊全域 `IDynamicsWebApiClient`/`IDynamicsHttpTransport`/`IAdfsOAuthTokenProvider`，且 `AddSqlRuntimeHostSlotCoordinator` 在 Program.cs 中晚於 Profile 註冊呼叫時會 `RemoveAll<IRuntimeHostSlotCoordinator>()` 後重新註冊，由於 DI 為延遲解析，實際執行時會正確取得 SQL 版 Coordinator，非 In-Memory 版本。
- `DynamicsGatewayReadinessService`（`IHostedService`）在非 Testing 環境正確於 `StartAsync` 呼叫 `_runtimeManager.InitializeAsync`、`StopAsync` 呼叫 `DisposeAsync`；Testing 環境測試改以 Stub Manager 注入，不依賴此 Hosted Service，符合預期。

---

## Info 🔵

- **繁體中文文件覆蓋**：所有新增/大幅修改的正式與測試程式碼（Manager、Runtime、Factory、Registry、TokenProvider、Executor、DI、測試檔）都具備具體、非空泛的繁體中文 XML 文件註解，說明 ownership、鎖定範圍與失敗語意，符合要求。
- **UTF-8 無 BOM + CRLF**：對 `DynamicsProfileRuntimeManager.cs`、`OrganizationAdmissionRegistry.cs`、`Phase4IsolationSoakTests.cs` 的 `file`/`xxd` 抽查確認皆為「UTF-8 text, with CRLF line terminators」且開頭無 BOM bytes，與任務聲明的批次檢查結果一致。
- **`appsettings.json`**：目前 `DynamicsProfiles:Profiles` 只設定 `crm82` 一個 Profile，`crm91` 尚未加入正式設定——這屬於當前階段刻意未完成的部署設定，不視為程式缺陷。
- 未發現 crm82／crm91 之間有任何可變 Client／Handler／Token／Credential／Metadata／Session 共用；兩者僅在指向同一實體 Organization 時透過 `OrganizationAdmissionRegistry` 共用 Admission 容量，符合意圖行為。
- Readiness（`/ready`）輸出僅包含 Alias、Generation、狀態與 bounded Admission 指標，測試 `Ready_endpoint_exposes_only_redacted_multi_profile_diagnostics` 已驗證不含 Endpoint、Secret、Credential、Token。

---

## Summary

**Request changes.** 除上述一項 Critical（Replace/Drain 失敗路徑會讓 Alias 永久失去熱替換能力並在 Catalog 中殘留已回收的強引用）外，其餘生命週期、隔離、容量與 DI 邏輯經逐檔核對均與 Intended Behavior 一致，且測試具備充分的生產路徑忠實度（非套套邏輯）。建議先修正 `ReplaceCoreAsync` 的 drain-failure 清理語意並補上對應的失敗注入測試，再合併本次變更。

---
SESSION_ID: a25adeed-5199-4f53-a0a0-b24a86596842
