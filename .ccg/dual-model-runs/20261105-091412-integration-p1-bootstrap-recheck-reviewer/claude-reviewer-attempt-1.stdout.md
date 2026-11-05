# 審查結果：P1 Milestone 1（smoke / P1-01 / P1-02）— 修正後完整包重審

## 先處理前次 Critical：證據缺口是否已解決

**結論：已解決，不再是 Critical。**

前次審查標記的 Critical 是「缺少 production/fixture/sln 證據」，根因是 `git diff HEAD` 在 f5c69cde/82515aab 於 09:01 被提交後變成空 diff，不是程式碼本身有缺陷。本包已改用固定的 P0 基準（cb932c01）重新產生 diff，並補上：
- `DeploymentEngine.cs`、`DeploymentContractTests.cs`、`SpeechMessage.Localization.Tests.csproj`、`SpeechMessageProducts.sln` 的實際 diff
- `DeploymentEngine.cs` smoke 相關原始碼（18-70、296-359、785-834 行）
- `Fixture`/`MemoryOrganization`/`AdvancingClock` 完整原始碼

這些證據內部自洽（例如 `AdvancingClock` 的 `now += TimeSpan.FromMinutes(2)` 舊寫死跳轉被改為 `now += dueTime`，是比舊版更貼近真實輪詢語意的修正，而非弱化）。經逐行核對輪詢次數、時鐘推進、cleanup 斷言，邏輯一致，**證據缺口本身視為已補足**。

## 逐項技術核對（correctness / lifecycle / isolation）

- **Smoke 2 秒輪詢契約**：`SmokeAsync` 先立即查詢，無結果才 `Task.Delay(2s)`，`CancellationTokenSource(TimeSpan.FromMinutes(3), clock)` 建立總期限。以 `AdvancingClock` 逐步推算：90 次輪詢 × 2 秒 = 180 秒剛好與逾時同時觸發，`TraceQueryCount`/`DelayCount` 斷言與引擎行為一致，poll=1/poll=3 情境的立即成功與多次重試都對得上。cancellation 與 query-failure 兩個案例也驗證了 `finally` 刪除 task、timer 數歸零，preflight 兩個拒絕路徑（已取消 token、未核定 scenario）都在建立 task/timer 之前就丟例外，符合「不得建立任何業務記錄或 timer」要求。未發現邏輯缺陷。
- **R4 架構規則**（`ArchitectureRules.cs`）：新增的 `ProductTarget` 守衛對任何非測試專案（含孤立、無人參考的節點）直接擋下非 net462/net10.0 目標，不依賴是否已被 Plugin/其他 core 參考，確實補上「未被消費的 net48 production」死角。`DualTargetReference`／`DualTargetPackage`／`SandboxReference`／`SandboxTarget`／`HostReference`／`LayerDirection`／`DeploymentIsolation`／`UnknownProject` 各規則與對應單元測試（合成圖 + 真實 XML fixture）逐一核對後行為一致，`ToolUtility.Core` 的 Dataverse 例外確實限定在 `net10.0` target，`net462` 下同一套件仍被攔截。
- **`ArchitectureProjectReader`**：以 `dotnet msbuild -getProperty/-getItem`（無 `-getTargetResult`）做純評估，不觸發 Build/Restore，與「唯讀評估」宣稱一致；`ValidateFrameworkAssets` 針對 net462（及 net48 測試宿主）驗證 `project.assets.json` 內 project 參考確實解析為 `.NETFramework,Version=v4.6.2`，覆蓋「NuGet 實際選擇」而非只信 csproj 宣告。
- **Fixture 執行緒安全**：`CollectionQueryServiceAsyncTests.QueryFixture` 的 `calls`/`activeCalls` 已用 `Interlocked`/`Volatile` 正確處理並發（`ConcurrentAsyncQueries_ShouldNotBlock` 會真的並發呼叫），符合「thread-safe fixture counters」宣稱。
- **P1-01 孤兒目錄**：`P1-01-reference-audit.md` 的 51 個待刪檔案逐一核對（LinePayCSharp 35 檔、Trace 9 檔、Net10 兩個 csproj、4 個 Crm82/91Worker 的 packages.lock.json）加總剛好 51，四個 worker 目錄確認「實際僅 packages.lock.json」，無 csproj/原始碼殘留，刪除前查核覆蓋也完整。

## Warning 🟡

1. **`tests-shared/ArchitectureTests/ArchitectureProjectReader.cs` `Evaluate` 方法**（約第 100-140 行區塊，timeout 分支與 `finally`）：逾時後呼叫 `process.Kill(entireProcessTree: true)` 再 `process.WaitForExit()`（無逾時參數）。若作業系統未能及時終止程序樹（例如防毒軟體掛鉤、子程序忽略終止信號），`WaitForExit()` 會無限期阻塞，使「per-process 30s timeout」的保證在罕見情況下失真。建議改為 `process.WaitForExit(5000)` 並在逾時仍未退出時明確回報失敗，而非無界等待。

2. **`CollectionQueryServiceAsyncTests.QueryFixture.LastQuery`**：在 `RetrieveMultiple` 內以一般欄位賦值（非 `Interlocked`/`volatile`），但該方法會被 `ConcurrentAsyncQueries_ShouldNotBlock` 並發呼叫。目前沒有測試在並發情境下讀取 `LastQuery`，所以不是現有迴歸風險，但屬資料競賽（data race）隱患——未來若有人替並發測試加上 `LastQuery` 斷言，會得到不穩定（flaky）結果。建議加註「並發測試不得依賴 LastQuery」或改為執行緒安全持有。

## Info 🔵

1. **`ToolUtility.Tests.csproj` 內容未附於本包**：`.sln` diff 顯示新增了此專案，但看不到它是否也以 `<Compile Include="../tests-shared/ArchitectureTests/*.cs">` 方式連結共用 R4 原始碼。若同時連結，`Solution_AllEvaluatedTargets_ObeyR4` 這類會啟動多個 `dotnet msbuild` 子程序的測試將在兩個組件中各跑一次，徒增 CI 時間且無額外訊號。請確認是否重複連結。
2. **`CollectionQueryService` / `ICollectionQueryService` production 原始碼未附於本包**，因此無法獨立驗證 `new CollectionQueryService(new object(), source)` 建構式型別相容性，以及 `pagingCookie:`/`cancellationToken:` 具名參數與實際方法簽章是否一致。這部分依賴任務敘述的「net10 22/22、net48 反射 1/1」驗證結果，本次審查未能以程式碼直接複核。

## 總結

未發現 Critical 等級缺陷；前次因 diff 證據缺失產生的 Critical 已因本包補齊 diff/完整原始碼而解除。Smoke 輪詢節奏、R4 規則與其測試、P1-01 孤兒刪除證據三者邏輯互相一致，執行緒安全計數器符合宣稱。遺留兩項 Warning（子程序逾時後的無界等待、`LastQuery` 的資料競賽隱患）建議在不影響本次 milestone 判定的前提下於後續小修修補；兩項 Info 屬於本包未附檔案造成的驗證邊界，待該檔案納入審查範圍時再次確認。Full-solution gate 仍待執行，與本次審查結論無衝突。

---
SESSION_ID: aae0decf-a29f-4009-88e8-d1f1fc3c537c
