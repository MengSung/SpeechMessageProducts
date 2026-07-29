# Dynamics Multi-Profile Runtime Drain Recovery 分析報告

本報告針對 `SpeechMessage.Dynamics` 系統平台中，關於多設定檔執行期（Multi-Profile Runtime）在動態替換與排空（Replace-and-Drain）過程中的非同步生命週期、資源隔離與異常復原機制進行深入分析。

---

## 1. UX Analysis (使用者影響評估)

### 1.1 運維與系統可用性影響
當系統管理員或自動化配置服務嘗試更新 Dynamics 365 組織設定檔（例如更新 CRM 憑證、變更端點 URL 或調整並行限制）時，會觸發 `ReplaceAsync` 操作。
- **現有問題的 UX 衝擊**：若舊的 Runtime 在執行 `DrainAndDisposeAsync` 時，因為暫時性的網路波動或外部服務（如 ADFS 憑證伺服器）無回應而拋出清理異常，目前的 Manager 會因為未清除 `slot.Draining` 引用，導致該 Slot 永久鎖定在「替換中」的狀態。
- **使用者旅程中斷**：後續任何重新套用設定的嘗試都會被系統以 `InvalidOperationException` 拒絕。管理員將無法透過再次儲存或重試來修復此狀態，唯一恢復服務的方法是重啟整個 Gateway 進程。這對於需要高可用性的語音訊息系統而言，會造成嚴重的服務中斷與不良的運維體驗。

### 1.2 客戶端請求影響
在 Slot 鎖死期間，依賴該設定檔的所有 outbound Dynamics 請求將會因為無法取得相容的 Active Runtime，而持續收到 `NotReady` 或 `CapacityRejected` 錯誤，導致前端業務功能失效。

---

## 2. Design Evaluation (設計系統評估)

### 2.1 狀態機與資源隔離一致性
根據 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 的規範：
- **單一 Alias 資源限制**：任一時間最多只能存在一個 Active 與一個 Draining Generation。這是為了防止多個 Generation 同時運行，對 Dynamics 伺服器造成超出預期的連線負載。
- **現有實作的缺陷**：
  - 當清理失敗時，狀態機未能正確收斂。
  - 若在舊 Runtime 尚未完成 `Disposed` 之前就將其從 Catalog 中移除，會導致該 Runtime 變成「孤兒（Orphaned）」，其背景排空任務仍在運行，但 Manager 已失去對其引用的控制，這打破了「最多一個 Draining」的架構約束。

### 2.2 視覺與監控一致性
Manager 的 `GetSnapshot()` 用於向監控儀表板提供系統健康狀態。若 `slot.Draining` 殘留，監控系統會持續回報該設定檔處於不健康的過渡狀態，干擾運維人員的告警判斷。

---

## 3. Technical Considerations (技術架構與風險分析)

### 3.1 Root Cause 與狀態機分析

在現有的 `DynamicsProfileRuntimeManager.ReplaceCoreAsync` 中，狀態轉移邏輯如下：

```csharp
// 舊 Active 被移至 Draining，並開始排空
previous = slot.Active;
slot.Active = candidate;
slot.Draining = previous;
previous.BeginDrain();
published = true;

// 執行排空與釋放
await previous.DrainAndDisposeAsync(callerCancellationToken).ConfigureAwait(false);

// 若上述 await 拋出例外，此處將無法執行，slot.Draining 永遠殘留 previous 引用
lock (_gate)
{
    if (ReferenceEquals(slot.Draining, previous))
    {
        slot.Draining = null;
    }
}
```

#### 狀態機的兩種關鍵狀態區分：
1. **狀態 1：舊 Runtime 已完成 `Disposed`，但資源清理回報例外**
   - **行為**：`previous.State` 已切換為 `Disposed`，但 `DisposeOwnedResourcesAsync` 中的 Token Provider、Transport 或 Admission Registration 釋放時拋出例外。
   - **處置**：Manager 必須向上回報此例外（不可吞掉），但**必須清除** `slot.Draining` 的強引用。因為該 Runtime 已經無法再使用且已標記為 Disposed，後續的 Replace 應該被允許建立新的 Generation。
2. **狀態 2：舊 Runtime 尚未 `Disposed`（如 Caller Cancellation 或排空逾時）**
   - **行為**：`previous.State` 仍為 `Draining`，主因是 active lease 尚未釋放，或等待過程被取消。
   - **處置**：Manager **不可**清除 `slot.Draining` 引用，亦**不可**呼叫 Factory 建立第三套 Runtime。後續的 Replace 必須先重試清理既有的 `slot.Draining`，成功後才可配置下一個 Generation。

### 3.2 鎖競爭與非同步邊界 (Lock Ordering & Async Boundary)
- **鎖內不 await**：所有狀態變更必須在 `lock (_gate)` 內完成，而非同步的 I/O 操作（如 `CreateValidatedRuntimeAsync` 與 `DrainAndDisposeAsync`）必須在鎖外進行。
- **非同步邊界發布**：`ReplaceCoreAsync` 必須在開始執行任何同步邏輯前，透過 `await Task.Yield()` 釋放執行緒，確保其 Task 能夠立即發布給呼叫者，避免在特定 `SynchronizationContext` 下產生死鎖。

---

## 4. Options (替代方案評估)

### 方案 A：在 Factory 分配前重試既有 Draining，並於 Finally 區塊精確清除（推薦）
- **設計**：
  1. `ReplaceAsync` 僅在 `ReplacementInProgress == true` 時拒絕。
  2. `ReplaceCoreAsync` 在呼叫 Factory 之前，先檢查並等待既有的 `slot.Draining` 完成排空。
  3. 在 `finally` 區塊中，僅在 `runtime.State == Disposed` 且引用一致時清除 `slot.Draining`。
  4. 成功清除後，才在鎖內遞增 `LastGeneration` 並配置新 Runtime。
- **優點**：完全符合狀態機安全，避免 Generation 號碼跳號，且在 Factory 分配前阻止第三套 Runtime。
- **權衡**：實作邏輯較為複雜，需要精確處理多個 `finally` 區塊與鎖的配合。

### 方案 B：強制清除並忽略清理異常
- **設計**：無論舊 Runtime 是否成功 Disposed，一旦 `ReplaceCoreAsync` 拋出例外，皆強制將 `slot.Draining` 設為 `null`。
- **優點**：實作簡單，後續 Replace 絕不鎖死。
- **缺點**：會導致未完成的舊 Runtime 變成懸空孤兒，造成嚴重的連線與記憶體洩漏，違反資源隔離原則。

---

## 5. Recommendation (建議方案與實作步驟)

### 5.1 最小安全實作步驟

#### 步驟 1：修改 `ReplaceAsync` 的准入檢查
放寬對 `slot.Draining` 的立即拒絕，僅在 `ReplacementInProgress` 為 `true` 時拒絕，以允許後續的 Replace 操作能夠重試清理。

```csharp
public Task ReplaceAsync(
    DynamicsProfileDefinition definition,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(definition);

    ProfileSlot slot;
    lock (_gate)
    {
        ThrowIfDisposeStarted();
        if (!_ready)
        {
            throw new InvalidOperationException("Dynamics profile runtime manager is not ready.");
        }

        if (!_slots.TryGetValue(definition.ProfileAlias, out slot!))
        {
            throw new KeyNotFoundException("The requested Dynamics profile alias is not registered.");
        }

        // 僅在已有另一個替換擁有者時拒絕，允許對既有 Draining 進行重試與等待
        if (slot.ReplacementInProgress)
        {
            throw new InvalidOperationException(
                "The Dynamics profile already has a replacement generation in progress.");
        }

        slot.ReplacementInProgress = true;
        BeginLifecycleOperationLocked();
    }

    // 將 generation 的遞增延遲至 ReplaceCoreAsync 中確定無 pending drain 後執行
    return ReplaceCoreAsync(slot, definition, cancellationToken);
}
```

#### 步驟 2：重構 `ReplaceCoreAsync` 流程
在 `ReplaceCoreAsync` 中，先等待既有的 `slot.Draining`，成功後才遞增 Generation 並建立新 Runtime。

```csharp
private async Task ReplaceCoreAsync(
    ProfileSlot slot,
    DynamicsProfileDefinition definition,
    CancellationToken callerCancellationToken)
{
    // 確保非同步邊界，使 Task 能夠立即發布給呼叫者，避免同步阻塞
    await Task.Yield();

    IDynamicsProfileRuntime? pendingDraining = null;
    lock (_gate)
    {
        pendingDraining = slot.Draining;
    }

    // 步驟 A：若存在既有的 Draining Runtime，先嘗試等待並清理它
    if (pendingDraining is not null)
    {
        try
        {
            await pendingDraining.DrainAndDisposeAsync(callerCancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                // 狀態 1：若舊 Runtime 確實已 Disposed，不論清理是否拋出例外，皆清除 Slot 引用
                if (pendingDraining.State == DynamicsProfileRuntimeState.Disposed &&
                    ReferenceEquals(slot.Draining, pendingDraining))
                {
                    slot.Draining = null;
                }
            }
        }
    }

    IDynamicsProfileRuntime? candidate = null;
    IDynamicsProfileRuntime? previous = null;
    var published = false;
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        callerCancellationToken,
        _shutdownCts.Token);

    try
    {
        long generation;
        lock (_gate)
        {
            ThrowIfDisposeStarted();

            // 步驟 B：確保既有的 Draining 已經成功清除。若仍存在，說明清理未完成（狀態 2）
            if (slot.Draining is not null)
            {
                throw new InvalidOperationException(
                    "Cannot allocate a new generation because the previous draining generation is still active.");
            }

            // 步驟 C：此時才遞增 Generation 號碼，避免失敗重試製造不必要的 Generation gap
            generation = checked(++slot.LastGeneration);
        }

        // 步驟 D：呼叫 Factory 建立並驗證新的 Runtime
        candidate = await CreateValidatedRuntimeAsync(
            definition,
            generation,
            linkedCts.Token).ConfigureAwait(false);

        lock (_gate)
        {
            ThrowIfDisposeStarted();
            if (slot.Draining is not null || slot.Active is null)
            {
                throw new InvalidOperationException(
                    "The Dynamics profile slot changed while the replacement generation was being built.");
            }

            previous = slot.Active;
            slot.Definition = definition;
            slot.Active = candidate;
            slot.Draining = previous;
            previous.BeginDrain();
            published = true;
        }

        // 步驟 E：清理剛被替換的舊 Active（現在是 Draining）
        await previous.DrainAndDisposeAsync(callerCancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (ReferenceEquals(slot.Draining, previous))
            {
                slot.Draining = null;
            }
        }
    }
    catch
    {
        if (!published && candidate is not null)
        {
            await candidate.DisposeAsync().ConfigureAwait(false);
        }
        throw;
    }
    finally
    {
        lock (_gate)
        {
            slot.ReplacementInProgress = false;
        }
        EndLifecycleOperation();
    }
}
```

---

## 6. 必要測試與競態案例 (RED Tests)

為了驗證上述修復的正確性，必須在 `MultiProfileRuntimeTests.cs` 中加入以下兩個測試案例：

### 6.1 測試案例 1：舊 Runtime 完成 Dispose 後注入 cleanup failure
- **目的**：驗證當舊 Runtime 已經進入 `Disposed` 狀態，但其資源清理拋出例外時，第一次 Replace 應該回報錯誤，但 `slot.Draining` 應該被清除，使得第二次 Replace 可以成功配置新的 Generation。
- **實作程式碼**：
```csharp
/// <summary>
/// 驗證當舊 Runtime 已完成 Dispose 狀態，但其資源清理（如 Token Provider 或 Transport）拋出例外時：
/// 1. 第一次 Replace 應回報該清理失敗例外。
/// 2. Slot 中的 Draining 引用必須被清除，不再保留舊 Generation。
/// 3. 第二次 Replace 能夠成功配置下一個 Generation。
/// </summary>
[Fact]
public async Task Replace_WithCleanupFailure_ClearsDrainingSlot_AndAllowsSubsequentReplace()
{
    // Arrange
    await using var factory = new TrackingRuntimeFactory();
    await using var manager = CreateManager(factory, CreateDefinition("crm91"));
    await manager.InitializeAsync(CancellationToken.None);

    var oldRuntime = factory.GetRuntime("crm91", 1);
    var cleanupFailure = new InvalidOperationException("Injected crm91 runtime cleanup failure.");
    factory.FailNextRuntimeDisposal("crm91", cleanupFailure);

    // Act - 第一次 Replace 應因為注入的清理失敗而拋出例外
    var firstReplace = async () => await manager.ReplaceAsync(CreateDefinition("crm91"));
    (await firstReplace.Should().ThrowAsync<InvalidOperationException>())
        .WithMessage("Injected crm91 runtime cleanup failure.");

    // Assert - 舊 Runtime 狀態應為 Disposed，且 Manager 快照中不應再有 Draining 節點
    oldRuntime.State.Should().Be(DynamicsProfileRuntimeState.Disposed);
    var snapshotAfterFailure = manager.GetSnapshot();
    snapshotAfterFailure.Profiles.Should().ContainSingle(p => p.Key.Generation == 2); // 僅剩新 Active
    snapshotAfterFailure.Profiles.Any(p => p.Key.Generation == 1).Should().BeFalse();

    // Act - 第二次 Replace 應能成功執行，並配置 Generation 3
    var secondReplace = async () => await manager.ReplaceAsync(CreateDefinition("crm91"));
    await secondReplace.Should().NotThrowAsync();

    // Assert - 驗證 Generation 3 已成功發布
    manager.GetSnapshot().Profiles.Should().Contain(p => p.Key.Generation == 3);
}
```

### 6.2 測試案例 2：第一次 Replace 在舊 Runtime 尚有 active lease 時由 caller cancellation 中止
- **目的**：驗證當舊 Runtime 尚有 active lease，且 Replace 操作被 caller cancellation 中止時，舊 Runtime 保持 `Draining` 狀態（未 Disposed）。第二次 Replace 必須先等待舊 Runtime 的 cleanup，且在等待期間 Factory `CreateCount` 不得增加，直到釋放舊 lease 後才建立下一個 Generation。
- **實作程式碼**：
```csharp
/// <summary>
/// 驗證當第一次 Replace 因為舊 Runtime 仍有作用中的租約（Active Lease）而被 Caller 取消時：
/// 1. 舊 Runtime 保持 Draining 狀態，且 Slot 中的 Draining 引用不得被清除。
/// 2. 第二次 Replace 必須先等待舊 Runtime 清理完成。
/// 3. 在等待期間，Factory 的 CreateCount 不得增加（避免 Generation 跳號）。
/// 4. 釋放舊租約後，第二次 Replace 才能成功建立下一個 Generation。
/// </summary>
[Fact]
public async Task Replace_CancelledDuringDrain_RetainsDrainingSlot_AndNextReplaceWaitsAndSucceeds()
{
    // Arrange
    await using var factory = new TrackingRuntimeFactory();
    await using var manager = CreateManager(factory, CreateDefinition("crm91"));
    await manager.InitializeAsync(CancellationToken.None);

    var oldRuntime = factory.GetRuntime("crm91", 1);
    oldRuntime.TryAcquireExecution(out var heldOldLease).Should().BeTrue();

    // 建立一個已被取消的 CancellationToken 來模擬 Caller Cancellation
    using var cancelledCts = new CancellationTokenSource();
    await cancelledCts.CancelAsync();

    // Act - 第一次 Replace 傳入已取消的 Token，應立即拋出 OperationCanceledException
    var firstReplace = async () => await manager.ReplaceAsync(CreateDefinition("crm91"), cancelledCts.Token);
    await firstReplace.Should().ThrowAsync<OperationCanceledException>();

    // Assert - 舊 Runtime 仍處於 Draining 狀態，且未被 Disposed
    oldRuntime.State.Should().Be(DynamicsProfileRuntimeState.Draining);
    oldRuntime.DisposeCount.Should().Be(0);
    manager.GetSnapshot().Profiles.Any(p => p.Key.Generation == 1).Should().BeTrue();

    // Act - 啟動第二次 Replace（不帶 Cancellation），此時它應該會等待舊租約釋放
    var secondReplaceTask = manager.ReplaceAsync(CreateDefinition("crm91"));

    // 確保第二次 Replace 正在等待，且 Factory 尚未建立 Generation 3
    await Task.Delay(100);
    secondReplaceTask.IsCompleted.Should().BeFalse();
    factory.CreateCount.Should().Be(2); // 僅有 Gen 1 (Init) 與 Gen 2 (第一次 Replace 建立的 Active)

    // 釋放舊租約，觸發排空完成
    await heldOldLease!.DisposeAsync();

    // 等待第二次 Replace 完成
    await secondReplaceTask;

    // Assert - 舊 Runtime 成功 Disposed，且 Generation 3 成功建立
    oldRuntime.State.Should().Be(DynamicsProfileRuntimeState.Disposed);
    oldRuntime.DisposeCount.Should().Be(1);
    factory.CreateCount.Should().Be(3); // Gen 1, Gen 2, Gen 3
    manager.GetSnapshot().Profiles.Should().Contain(p => p.Key.Generation == 3);
}
```

---

## 7. 風險分級與評估 (Risk Classification)

### 7.1 Critical (嚴重風險)
- **排空異常導致永久鎖死**：
  - *路徑*：`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs` 中的 `ReplaceCoreAsync`。
  - *成因*：`previous.DrainAndDisposeAsync` 拋出例外時，`slot.Draining = null` 的清除邏輯被跳過，導致後續的 `ReplaceAsync` 永久被拒絕。
  - *後果*：系統無法自我修復，必須重啟進程。
- **懸空孤兒 Runtime 導致資源洩漏**：
  - *路徑*：同上。
  - *成因*：若在舊 Runtime 尚未 Disposed 之前（如 Cancellation 發生時）就將 `slot.Draining` 設為 `null`，該 Runtime 將失去強引用，其持有的連線與信號量將永遠無法釋放。

### 7.2 Warning (中度風險)
- **Generation 號碼跳號 (Generation Gap)**：
  - *路徑*：`ReplaceAsync` 與 `ReplaceCoreAsync` 的交接處。
  - *成因*：若在等待舊 Runtime 清理之前就遞增了 `LastGeneration`，一旦清理失敗，下一次重試會再次遞增，導致 Generation 號碼不連續，影響日誌追蹤與除錯。
- **非同步邊界缺失導致死鎖**：
  - *路徑*：`ReplaceCoreAsync` 的開頭。
  - *成因*：若未在方法開頭使用 `await Task.Yield()`，在同步呼叫或特定 SynchronizationContext 下，可能導致 Task 無法及時發布給呼叫者，進而引發死鎖。

### 7.3 Info (一般資訊)
- **繁體中文 XML 註解規範**：
  - *路徑*：所有新增與修改的程式碼。
  - *成因*：根據 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 的要求，所有新增的非同步生命週期方法必須包含詳盡的繁體中文 XML 註解，說明其所有權、並行行為與清理路徑。
