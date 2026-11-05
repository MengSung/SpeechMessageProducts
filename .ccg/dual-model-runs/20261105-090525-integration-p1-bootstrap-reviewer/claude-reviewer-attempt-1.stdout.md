## 審查結果：integration-p1-bootstrap（smoke / P1-01 / P1-02）

### 🔴 Critical

1. **證據缺口 — DeploymentEngine.cs / DeploymentContractTests.cs / SpeechMessageProducts.sln 的 diff 為空**
   - 檔案：`SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs`、`SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs`、`SpeechMessageProducts.sln`
   - 失敗情境：`SmokePollingTests.cs` 測的是 `f.Engine.SmokeAsync(...)`，但本封包完全沒有附上 `DeploymentEngine`／`Fixture`／`FakeClock`／`FakeService` 的實作。若實際實作裡取消傳播、timer 歸零、task 刪除時機與測試假設（例如 `AssertCleaned` 依賴恰好一筆 `task` 列且 `Delete:task` 恰好一次）不一致，測試仍可能對著錯誤的假物件通過，卻無法代表 `DeploymentEngine` 本身正確。同理，`SpeechMessageProducts.sln` 的空 diff 代表「ToolUtility.Tests added to solution」與「deployment 62/62」等驗證聲明在本封包中不可被覆核。
   - 建議：補上 `DeploymentEngine.SmokeAsync`、`Fixture`/假 SDK/假時鐘類別，以及 `.sln` 實際新增的項目行，否則此里程碑的核心生產程式碼完全未受審查，僅審到測試本身的內部一致性。

### 🟡 Warning

1. **P1-01 孤兒稽核對 Crm82Worker / Crm91Worker 的敘述與刪除清單不一致**
   - 檔案：`wp-reports/P1-01-reference-audit.md`
   - 失敗情境：稽核寫「搜尋：SpeechMessage.Dynamics.Crm82Worker / 結果：0 個外部參考」、「SpeechMessage.Dynamics.Crm91Worker」亦同，語氣暗示整個專案是孤兒；但「待刪除追蹤檔」清單裡這兩個專案只列出 `packages.lock.json`，csproj 與原始碼都不在刪除範圍。若這兩個專案其實仍在方案中存活（例如仍被 `.sln` 收錄、只是暫時沒有程式碼參考），「0 個外部參考」的措辭會誤導後續維護者以為可以整批刪除整個目錄；若它們真的是孤兒，則刪除清單漏掉了 csproj/原始碼，將造成死碼殘留在方案中。
   - 建議：在報告中明確註明「專案仍在 .sln 中保留（原因 X），僅移除無主 lock 檔」或「專案將於後續 PR 整體移除」，避免兩種解讀同時成立造成誤刪或漏刪。

2. **R4 規則對「雙目標產品」框架宣告本身缺乏直接驗證，只能透過被參考方關係間接抓到**
   - 檔案：`tests-shared/ArchitectureTests/ArchitectureRules.cs`（`dual` 判定、`DualTargetReference`/`SandboxReference` 檢查）
   - 失敗情境：若新增一個本應是 net462;net10.0 的雙目標產品，卻誤宣告成 `net48;net10.0`，且當下沒有任何 sandbox 或既有 dual 專案去參考它（例如只被某個 net10-only 的 host 或尚未串接的新模組參考），目前的規則集完全不會觸發任何 Violation——因為所有檢查都是「參考方要求被參考方必須同時具備 net462 與 net10.0」，而非「每個非 sandbox/非 host/非 test 的產品都必須恰好宣告 net462;net10.0」。這代表 R4 對框架正確性的覆蓋是被動（reference-driven），尚有未被任何現有消費者觸及的死角。
   - 建議：視milestone範圍是否接受此限制；若要封閉此洞，需新增一條「非 test、非 host 的 production 專案，其 Targets 必須恰為允許集合（如 net462;net10.0 或單一 net10.0）」的直接規則。

3. **`CollectionQueryServiceAsyncTests` 中以 Stopwatch 牆鐘時間與 `GC.GetTotalMemory` 做斷言，屬於非決定性斷言**
   - 檔案：`ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs`
     - `RetrieveBatchByIdsAsync_WithLargeSet_ShouldHandleEfficiently`：`watch.Elapsed < TimeSpan.FromSeconds(5)`
     - `PagedQuery_MemoryUsage_ShouldBeLow`：`GC.GetTotalMemory(true) - before < 50MB`
   - 失敗情境：在 CI runner 負載高、xunit 類別並行執行、或恰好觸發背景 GC/JIT 熱身時，即便程式邏輯完全正確也可能偶發性超過門檻而導致假性紅燈（flaky）。這類假 SDK 的純記憶體操作其實不需要真實計時或記憶體量測即可驗證「只有 1 次 I/O／結果已清空」，目前混用決定性斷言（`source.Calls`、`source.ActiveCalls`）與非決定性斷言（牆鐘、GC）。
   - 建議：若要保留粗略效能預算可保留，但應視為輔助性斷言而非唯一判斷依據（目前兩案例已經有決定性的 `source.Calls`/`ActiveCalls` 作為主斷言，牆鐘/記憶體屬輔助，風險可接受，故降為 Warning 而非 Critical）；長期建議移除或放寬門檻避免 CI 間歇性失敗。

4. **`QueryFixture.Calls` / `ActiveCalls` 非執行緒安全，正確性完全依賴「同步 SDK 包裝不會產生真正平行呼叫」的假設**
   - 檔案：`ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs` 內的 `QueryFixture.RetrieveMultiple`
   - 失敗情境：`ConcurrentAsyncQueries_ShouldNotBlock` 同時發出 10 個 `RetrieveEntityCollectionByFieldAsync`。測試註解自己承認「同步 SDK 的 Task 包裝不會憑空產生可取消的網路 I/O」，暗示目前實作其實是偽非同步（同步執行緒內完成）。若未來 `CollectionQueryService` 改為用 `Task.Run` 真正丟到執行緒池平行呼叫 `IOrganizationService.RetrieveMultiple`，`Calls++`/`ActiveCalls++`/`ActiveCalls--` 這三個非 `Interlocked` 的欄位會出現資料競爭，導致 `Assert.Equal(10, source.Calls)` 間歇性失敗，且不易被誤判為「假 SDK 的 bug」而非「生產程式碼真的開始平行化」的訊號。
   - 建議：至少在 `QueryFixture` 內用 `Interlocked.Increment/Decrement`，讓這個假設被打破時測試本身仍穩定，而不是變成一個會隨機抖動、難以診斷的計數器。

### 🟢 Info

1. **逾時臨界的最後一輪仍多等 2 秒才判定失敗**
   - 檔案：`SmokePollingTests.cs`（`Smoke_NoTrace_StopsAtThreeMinutesWithNinetyPolls`，隱含對應的 `DeploymentEngine.SmokeAsync` 迴圈結構）
   - 說明：從測試反推，迴圈結構應為 `while (elapsed < max) { query(); if success return; delay(2s); }`。第 90 次查詢失敗後仍會执行第 90 次 `delay`，把 elapsed 推到剛好 180s 才於下一輪迴圈頂端判斷逾時退出。邏輯一致、無不一致之處（先前判斷有誤已自我更正），但代表最壞情況下，從「已知不可能成功」到「真正回報逾時失敗」之間仍會多等 2 秒真實時間。可接受，純屬可優化項目（非缺陷）。

2. **`ArchitectureProjectReader.ReadSolution` 對每個專案 × 每個 target 都各啟動一次 `dotnet msbuild`**
   - 檔案：`tests-shared/ArchitectureTests/ArchitectureProjectReader.cs`
   - 說明：方案若有 26+ 專案、多數雙目標，`Solution_AllEvaluatedTargets_ObeyR4` 可能需要連續啟動數十個 `dotnet msbuild` 子程序（外加每個專案的一次無目標初評），在「全方案閘門」階段可能顯著拉長 CI 時間。目前每次呼叫均有獨立 30 秒逾時與確定釋放（`using process`、`finally` 中排水 stdout/stderr），資源生命週期本身沒問題，只是整體吞吐量值得留意。

3. **`DualTargetPackage` 的 ToolUtility.Core 例外以硬編字串比對專案名稱/target/套件三元組**
   - 檔案：`tests-shared/ArchitectureTests/ArchitectureRules.cs`
   - 說明：此例外寫死在規則引擎中（非資料驅動），符合 spec 要求的單一例外，但往後若需要新增類似例外會變成逐一硬編，可讀性尚可、可維護性略差，非本次缺陷範疇，僅供後續重構參考。

4. **`new object()` 作為 `CollectionQueryService` 建構子的 logger 佔位參數**
   - 檔案：`ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs`
   - 說明：測試注解承認這是補完原稿遺漏的初始化、純佔位。若建構子簽章實際預期特定 logger 介面型別而非 `object`，此處應無法編譯，故目前狀態隱含簽章本身即為寬鬆型別——值得在審到 `CollectionQueryService` 本體（未附於本封包）時一併確認是否為刻意設計。

5. **`Trace/Trace.csproj.new`** 這類備份/生成殘留檔出現在待刪清單中，屬於既有倉庫衛生問題，非本次新增程式碼缺陷，僅記錄供參考。

### 總結

本次封包內**測試自身的邏輯一致性良好**（smoke 輪詢節奏、15 個效能案例、R4 規則的正反例覆蓋都經得起推敲），但**最關鍵的生產程式碼（DeploymentEngine.SmokeAsync 實作與假 SDK/假時鐘）與 .sln 變更完全沒有附上 diff**，使本次審查只能驗證「測試對著測試自己期望的假物件是自洽的」，無法驗證「P1 milestone 聲稱的 smoke/部署/R4 驗證結果（62/62、15/15、22/22 等）是否真的由這份程式碼產出」。建議在下一輪送審時補齊 `DeploymentEngine.cs`、`DeploymentContractTests.cs` 的實際 diff 與 `.sln` 變更內容，其餘為 Warning/Info 級別的強化建議，不阻擋本里程碑，但建議在「Full-solution gate」前一併處理第 2、3、4 項的脆弱性。

---
SESSION_ID: d0832b92-46e5-495d-8157-f8b3b9577750
