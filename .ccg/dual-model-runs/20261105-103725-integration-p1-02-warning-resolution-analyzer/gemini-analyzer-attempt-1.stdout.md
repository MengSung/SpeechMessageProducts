# P1-02 Warning 根因分析與設計審查報告

依據 CCG 規範與使用者請求，針對 P1-02 雙模型審查所留下的兩項 Warning 及一項 Gemini 附加審查項進行唯讀程式碼檢視與深度根因分析。本報告不修改檔案、不套用 patch、不連線 D365。

---

## 總結摘要表

| 項次 | 檔案與方法 | 分級 | 根因摘要 | 可重現條件與影響 | 修正建議與優先級 |
|:---:|---|---|---|---|---|
| 1 | `tests-shared/ArchitectureTests/ArchitectureProjectReader.cs`<br>`Evaluate` | **Warning** | 超時呼叫 `process.Kill(entireProcessTree: true)` 後進行無期限 `process.WaitForExit()` 與 `finally` 區塊無界等待。 | 依賴作業系統未能即時終止行程樹（如防毒軟體掛鉤或子程序僵死）時，會造成測試執行緒無限期卡死。 | **高優先級**：改為有界等待（如 `WaitForExit(5000)`）並配合確定性清理與觀察。 |
| 2 | `ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs`<br>`QueryFixture.LastQuery` | **Warning** | 於 `RetrieveMultiple` 非同步並行查詢時以一般欄位（`QueryBase?`）無鎖寫入，具資料競賽（Data Race）隱患。 | 當有多個並行任務同時執行 `RetrieveMultiple` 且測試未來擴充對 `LastQuery` 的並發讀取斷言時，會出現 Flaky Test。 | **中優先級**：加註並發警示，或改以 `Interlocked` / `lock` / 執行緒安全持有（若需並發斷言）。 |
| 3 | `SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs`<br>`AdvancingClock.CreateTimer` | **Info** (非實質風險) | Gemini 假設 `CreateTimer` 同步快轉時間與觸發可能跨執行緒。 | 經程式碼與呼叫圖檢視，`AdvancingClock` 僅在單一同步測試執行緒（如 `SmokeAsync` 流程）中被循序呼叫，無真實跨執行緒競爭。 | **低優先級**：維持現狀，不需無謂重寫測試時鐘，僅補上同步呼叫聲明註解。 |

---

## 詳盡分析報告

### 1. `ArchitectureProjectReader.Evaluate` 子程序生命週期與逾時等待分析

#### 根因與技術審查
在 `tests-shared/ArchitectureTests/ArchitectureProjectReader.cs` 的 `Evaluate` 方法中（第 134-160 行）：
```csharp
try
{
    if (!process.WaitForExit(30_000))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(); // <-- 缺陷點 1：無界等待
        throw new TimeoutException("Read-only architecture evaluation exceeded its deadline.");
    }
    // ...
}
finally
{
    if (!process.HasExited)
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(); // <-- 缺陷點 2：finally 中的無界等待
    }
    Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
}
```
* **架構守門要求**：「每個程序有界等待、資源確定性釋放」。
* **風險**：雖然設定了 30 秒的全域逾時，但在逾時觸發或例外發生時，呼叫 `process.Kill(entireProcessTree: true)` 後緊接著呼叫無參數的 `process.WaitForExit()`。若作業系統的行程終止因驅動程式卡住或子程序句柄被子行程佔用而未立即釋放，該執行緒將永久阻塞，違反 P1-01 / P1-02 的有界等待原則。同時，`ReadToEndAsync` 透過 `Task.WhenAll(stdout, stderr)` 排空，但在行程僵死時其 Task 也可能無法完成。

#### 修正建議（最小實作）
1. 將無參數的 `process.WaitForExit()` 改為有界等待（例如 `process.WaitForExit(5000)`）。
2. 若 5 秒內仍未退出的話，記錄警告或直接放行（因已呼叫 Kill），避免測試進程無限卡死。
3. 確保 `stdout` 與 `stderr` 的排空加上逾時保護。

---

### 2. `CollectionQueryServiceAsyncTests.QueryFixture.LastQuery` 執行緒安全分析

#### 根因與技術審查
在 `ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs` 中，`QueryFixture` 的 `LastQuery` 以一般屬性實作：
```csharp
internal QueryBase? LastQuery { get; private set; }
// 在 RetrieveMultiple 中：
LastQuery = query;
```
* **可重現條件**：在 `ConcurrentAsyncQueries_ShouldNotBlock` 等並行測試情境中，多個執行緒同時呼叫 `RetrieveMultiple`。
* **實際影響**：目前現有測試並沒有在並發呼叫期間去讀取 `LastQuery`（多半是在非同步作業完成後才單執行緒讀取），因此在現行測試套件中不會直接引發紅燈。但這屬於標準的**資料競賽（Data Race）**。若未來的維護者在並發測試中加入即時讀取 `LastQuery` 的斷言，將導致難以偵測的 Flaky Test。

#### 修正建議
若不需在並發執行期間斷言 `LastQuery`，應在屬性上方補上 XML 註解明言：`/// 警告：非執行緒安全，僅供循序測試或作業完成後的最後一次查詢斷言使用。`；若未來有並發斷言需求，則改用 `System.Threading.Interlocked` 或對應的執行緒安全容器儲存。

---

### 3. `AdvancingClock` 虛擬時鐘與執行緒安全審查

#### 根因與審查判定
Gemini 先前提出 `SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs` 的 `AdvancingClock.CreateTimer` 在同步快轉時間並呼叫 `OnDelay` 時可能存在並發風險。

* **程式碼檢視**：
```csharp
public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
{
    var timer = new OwnedTimer(this, callback, state, dueTime);
    timers.Add(timer);
    ActiveTimers++;
    if (dueTime <= TimeSpan.FromSeconds(2))
    {
        DelayCount++;
        ObservedDelays.Add(dueTime);
        now += dueTime;
        OnDelay?.Invoke();
        foreach (var scheduled in timers.ToArray())
        {
            scheduled.FireIfDue(now);
        }
    }
    return timer;
}
```
* **實際呼叫圖與執行緒分析**：
  經查閱 `DeploymentEngine.SmokeAsync` 及其測試案例，`AdvancingClock` 僅在 xUnit 測試方法的**單一同步執行緒**中被 `DeploymentEngine` 呼叫。引擎本身在進行 2 秒輪詢時為完全循序的非同步迴圈（`await Task.Delay` 被 `TimeProvider` 攔截並同步快轉）。
* **判定結論**：**此 Warning 屬於假設性情境（Hypothetical Scenario）**。在目前的測試呼叫圖中**不存在跨執行緒並發呼叫**。因此**不建議**無謂地重寫測試時鐘或加上過度複雜的 lock，以免降低測試可讀性。僅需在類別說明中釐清其為單執行緒虛擬時鐘即可。

---

## 結論與後續行動

1. **ArchitectureProjectReader 修正**：優先修正 `Evaluate` 中的無界 `WaitForExit()`，落實 P1 既有的「每個程序有界等待、資源確定性釋放」規格。
2. **LastQuery 標示**：透過明確註解或輕量同步保護消除潛在的資料競賽隱患。
3. **AdvancingClock 確認**：確認其為單執行緒安全設計，無需重構。
