# P1-02 Warning 根因分析報告（唯讀檢查）

## Finding 1 — `ArchitectureProjectReader.Evaluate`：Kill 之後的 `WaitForExit()` 與 drain 均為無界等待

**檔案/方法**：`tests-shared/ArchitectureTests/ArchitectureProjectReader.cs:117-161`，`Evaluate`

**根因**：
- 逾時分支（136-140 行）：`process.Kill(entireProcessTree: true)` 之後呼叫無參數 `process.WaitForExit()`（139 行），此呼叫沒有期限。
- `finally`（151-160 行）：同樣模式重複一次（153-157 行），且其後的 `Task.WhenAll(stdout, stderr).GetAwaiter().GetResult()`（159 行）也沒有期限。
- 正常路徑的 drain（143 行）同樣是無界 `GetResult()`。

**可重現條件**：僅在 `Kill(entireProcessTree:true)` 未能讓目標程序即時退出時觸發（例如防毒掃描鎖住退出中的 `dotnet.exe`、權限被拒、或子程序在 Kill 快照後才建立而未被納入程序樹）。這在一般本機執行幾乎不會發生，但一旦發生，目前程式碼沒有任何期限兜底，會造成測試整個卡死，無法透過逾時機制恢復。

**實際影響**：違反背景任務列出的既有 P1 規格「每個程序有界等待、資源確定性釋放」。這不是規格本身的矛盾或不可行（Kill + 有期限 WaitForExit 完全可行），純屬實作未落實規格，不構成計畫缺陷。

**風險等級**：**Warning**（低機率觸發，但一旦觸發即為無期限掛起，且與既有規格字面衝突）

**最小修正建議**（不可用無界等待或遺留背景讀取工作掩蓋逾時）：
```csharp
private static void TerminateWithBoundedWait(Process process)
{
    process.Kill(entireProcessTree: true);
    if (!process.WaitForExit(5_000))
    {
        throw new TimeoutException("Process did not exit within the termination grace period.");
    }
}

private static void DrainWithBoundedWait(Task<string> stdout, Task<string> stderr)
{
    if (!Task.WhenAll(stdout, stderr).Wait(5_000))
    {
        throw new TimeoutException("Standard output/error drain exceeded its deadline after process exit.");
    }
}
```
把 139 行、156 行的 `WaitForExit()` 換成 `TerminateWithBoundedWait(process)`，143 行、159 行的 drain 換成 `DrainWithBoundedWait(stdout, stderr)`。終止與排空都改為顯式失敗（丟出可診斷的 `TimeoutException`），而不是無界阻塞或放任讀取工作在背景飄著。輸出記憶體上限（`ReadToEndAsync` 無上限）屬於 Info 等級的維護建議——輸入來源固定是受信任的本機 `dotnet msbuild -getProperty/-getItem`，預期輸出量小，非立即風險，可留待未來一併處理，不建議在本次最小修正中夾帶。

**先行測試（紅燈）**：目前沒有任何測試覆蓋 `Evaluate` 的逾時/Kill 路徑（`ArchitectureXmlTests.cs` 只呼叫 `ReadProjects` 走正常成功路徑）。真實程序被 `Kill` 後幾乎必定在毫秒級退出，無法在單元測試中可靠地偽造「Kill 後卡住」情境；因此對 Kill/WaitForExit 配對，建議以程式碼檢視取代紅燈測試即可。但 `DrainWithBoundedWait` 可用確定性紅燈測試驗證：給一個永不完成的 `Task<string>`（如 `Task.Delay(Timeout.Infinite).ContinueWith(_ => "")`），斷言在修正前此函式會無界掛起（或直接驗證修正後的版本於 5 秒內丟出 `TimeoutException`，而非真的等待）。

---

## Finding 2 — `QueryFixture.LastQuery` 並行寫入未同步

**檔案/方法**：`ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs:220`（屬性宣告）、`232-250`（`RetrieveMultiple` 寫入點）

**根因**：`LastQuery` 是一般 `{ get; private set; }` 自動屬性，`RetrieveMultiple`（238 行）以一般欄位寫入 `LastQuery = query;`，沒有 `volatile` 或 `Interlocked`／鎖保護；而同一類別中的 `calls`／`activeCalls` 兩個計數欄位都已用 `Interlocked`/`Volatile.Read` 保護（222-228 行），形成不一致。

**可重現條件 / 實際影響**：唯一的並行呼叫情境是 `ConcurrentAsyncQueries_ShouldNotBlock`（160-171 行），10 個並行 Task 共用同一個 `source`／`collectionService`。但該測試**完全沒有讀取 `source.LastQuery`**（只斷言 `Calls`、`ActiveCalls`、各自的 `result.Entities`），所以目前不存在任何會因此競態而產生錯誤斷言或崩潰的可重現失敗案例。另外，C#/ECMA-335 保證參考型別欄位寫入是原子的，不會有「撕裂寫入」；缺少 `volatile` 只是潛在的跨執行緒可視性延遲風險，而非資料損毀風險。

**風險等級**：**Warning**（目前不可重現、無實際斷言受影響，屬於潛伏的維護風險，非現行缺陷）

**最小安全發布建議**：改成手動 `volatile` 欄位，與既有 `calls`/`activeCalls` 風格一致：
```csharp
private volatile QueryBase? lastQuery;
internal QueryBase? LastQuery => lastQuery;
...
public EntityCollection RetrieveMultiple(QueryBase query)
{
    ...
    lastQuery = query;
    ...
}
```
**是否需要並行讀取斷言保護**：不需要。`LastQuery` 的語意本來就是「保留最後一個合成 query」，在 10 個並行呼叫下「最後一個」沒有確定順序，對其值加斷言只會製造不穩定（flaky）測試，與既有規範「以確定取消注入而非計時競賽」的精神相違。因此修正範圍僅止於欄位可視性，不應新增或改動既有並行測試的斷言，也不應降低現有斷言範圍。

**先行測試（紅燈）**：此修正屬於可視性保證而非行為修正，難以用確定性紅燈測試重現「可視性延遲」（JIT/CPU 重排序在 x86/x64 .NET 下極難於單元測試層級穩定重現）。建議以程式碼一致性檢視為準：修正後重跑 `ConcurrentAsyncQueries_ShouldNotBlock`、`PagedQuery_MemoryUsage_ShouldBeLow` 等既有測試確認綠燈不變即可，不需新增測試。

---

## Finding 3 — `AdvancingClock`（`DeploymentContractTests.cs:1388-1493`）：**不成立**

**Gemini 的推測**：`CreateTimer` 同步快轉時間並呼叫 `OnDelay`、觸發已到期 timer，建議加同步上下文保護。

**程式碼依據（判定不成立）**：
1. 對整份 `DeploymentContractTests.cs` 搜尋 `.OnDelay` / `OnDelay =`：**無任何測試賦值**（搜尋結果為零筆）。`OnDelay` 屬性目前只是定義但未被使用的擴充點，不構成現行呼叫圖的一部分。
2. 生產程式碼唯一消費這個 `TimeProvider` 的呼叫路徑是 `DeploymentEngine.SmokeAsync`（`DeploymentEngine.cs:306-350`）：
   - `new CancellationTokenSource(TimeSpan.FromMinutes(3), clock)`（314 行，長期限，不會觸發 165 行 `dueTime <= 2s` 的快轉分支）
   - `await Task.Delay(TimeSpan.FromSeconds(2), clock, linked.Token)`（335 行，`while` 迴圈內**依序 await**，不是 fire-and-forget 或並行觸發）。
   - 這個迴圈是單一邏輯執行緒的 async 狀態機：每次 `CreateTimer` 呼叫都是迴圈下一輪才會發生，彼此嚴格序列化，不存在兩個執行緒同時呼叫同一個 `AdvancingClock.CreateTimer` 的情形。
3. 測試檔中唯一的 `Task.Run`（`DeploymentContractTests.cs:525`）只用來在背景延遲後呼叫 `cancellation.Cancel()`，操作對象是獨立的 `CancellationTokenSource`，**不觸碰 `Clock`**，所以也不構成跨執行緒呼叫 `CreateTimer`/`now`/`timers` 的途徑。
4. `AdvancingClock` 的 `now`、`timers` 欄位確實沒有鎖保護，但因為每個測試各自 `new Fixture()`（1164 行）取得獨立 `Clock` 實例，且上述呼叫圖中不存在同一實例被多執行緒並行呼叫 `CreateTimer` 的情境，因此目前沒有資料競態的可重現觸發點。

**結論**：以現有測試呼叫圖判斷，不存在跨執行緒呼叫 `CreateTimer`／`OnDelay` 的情形，Gemini 標記的 Warning **不成立**。此為假設情境（若未來有測試把 `OnDelay` 設為會從另一執行緒重入 `Clock` 的委派，或多個測試並行共用同一個 `AdvancingClock` 實例），**不建議現在重寫測試時鐘**；若日後真的出現這種用法，再針對當時的實際呼叫圖評估是否需要同步保護。

---

## 彙總

| # | 項目 | 等級 | 是否成立 | 動作 |
|---|------|------|----------|------|
| 1 | `ArchitectureProjectReader.Evaluate` 的 `WaitForExit()`／drain 無界等待 | Warning | 成立 | 加入 5 秒期限的 `TerminateWithBoundedWait`／`DrainWithBoundedWait`，逾時即丟 `TimeoutException` |
| 2 | `QueryFixture.LastQuery` 未同步寫入 | Warning | 成立但目前無可觀測影響 | 改為 `volatile` 欄位，不需新增並行斷言 |
| 3 | `AdvancingClock` 同步快轉 + `OnDelay` 重入風險 | — | **不成立**（Gemini 推測與現行呼叫圖不符） | 不修改，維持現狀 |

三項都不觸及 D365 連線、憑證或部署動作；本次僅讀取檢視，未修改任何檔案。

---
SESSION_ID: 46b15bc5-175a-4158-a2f9-19529a5f5f54
