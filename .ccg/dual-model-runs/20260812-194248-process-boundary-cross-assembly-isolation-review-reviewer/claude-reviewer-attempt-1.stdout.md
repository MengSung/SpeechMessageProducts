# 跨程序集 WorkerTestHost 程序邊界隔離審查報告

審查範圍：`.trellis/tasks/08-12-process-boundary-cross-assembly-isolation/` 目前未提交（uncommitted）的實作，包含 `TestInfrastructure/WorkerTestHostProcessBoundaryCollection.cs`、`SpeechMessage.Dynamics.Tests/WorkerTestHostProcessBoundaryLeaseTests.cs`、兩個測試專案 `.csproj` 的 source-link 變更，以及所有改掛 `[Collection(WorkerTestHostProcessBoundaryCollection.Name)]` 的 process-boundary / OfficialWorker* 測試類別。

## 需求逐項核對

1. **同 worktree 共用一把鎖、跨 worktree 不互相阻塞、路徑不外洩**：符合。`BuildLockPathForTesting`（`WorkerTestHostProcessBoundaryCollection.cs:161-171`）以 canonical solution root（大寫、去尾端分隔符）的 SHA-256 雜湊前 16 hex 字元組成鎖檔名，鎖檔路徑本身不含原始路徑或使用者資料；`FindSolutionRoot()`（同檔 178-192 行）以 `SpeechMessageProducts.sln` 為 marker 向上尋找，本 worktree 根目錄下確實存在該 `.sln`，因此不同 worktree 會得到不同 partition。
2. **有界等待、只重試 Windows sharing/lock violation、其他 I/O 錯誤 fail closed 不誤判為 timeout**：符合。`Acquire()`（同檔 87-116 行）只在 `IsExpectedContention`（HResult 低 16 bits 為 32 或 33）為真時輪詢或最終拋 `TimeoutException`；其餘 `IOException`（含 `DirectoryNotFoundException`）與非 `IOException` 一律不攔截，直接向上傳遞，`WorkerTestHostProcessBoundaryLeaseTests.Non_contention_file_failure_is_not_retried_or_reclassified_as_a_timeout`（84-97 行）也驗證了這點。
3. **FileStream 單一 owner、無 static handle、OS 可回收 abort 的 testhost**：符合。`_stream` 為 instance 欄位並以 `Interlocked.Exchange` 做一次性 `Dispose`（同檔 57-66 行），沒有任何 static 欄位持有 handle；`FileShare.None` 搭配 OS-level handle 語意，process 異常終止時 OS 會自動釋放鎖。
4. **shared source 僅連結進測試組件、且所有相關 producer / zero-worker observer 都掛上同一 collection**：符合。兩個 `.csproj` 只新增 `Compile Include ... Link="Support\..."` 到測試專案；已核對 `DedicatedGatewayProcessBoundaryTests`、`FeatureDisabledDynamicsProcessBoundaryTests`、`OfficialWorkerControlPlaneAdmissionTests`、`OfficialWorkerProfileExecutorTests`、`OfficialWorkerSoakAndPerformanceTests` 五個會建立或斷言零 `WorkerTestHost` 的類別皆已改用 `WorkerTestHostProcessBoundaryCollection.Name`，未發現漏改的 producer/observer。
5. **未弱化斷言、未引入 cross-user/cross-profile/credential/process/資源洩漏路徑**：符合，未見任何既有斷言被刪除或放寬。

## Critical 🔴
無。

## Warning 🟡

1. **三個舊 `CollectionDefinition` 型別已成為死碼，且其 XML 注解內容具誤導性**
   - `SpeechMessage.Dynamics.Tests/DedicatedGatewayProcessBoundaryTests.cs:19-26`（`DedicatedGatewayProcessBoundaryCollection`）
   - `SpeechMessage.Dynamics.Tests/OfficialWorkerSoakAndPerformanceTests.cs:18-29`（`OfficialWorkerSoakTestCollection` / `OfficialWorkerSoakTestCollectionDefinition`）
   - `ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs:19-26`（`FeatureDisabledDynamicsProcessBoundaryCollection`）
   - 這三個型別在改用 `WorkerTestHostProcessBoundaryCollection.Name` 後已不再被任何 `[Collection(...)]` 引用，但其 XML 注解仍宣稱「此 collection 禁止平行執行」「保護程序邊界資源」。風險：日後新增 process-boundary 測試時若沿用這些舊名稱掛 `[Collection]`，會誤以為取得跨程序集互斥，實際上完全繞過 `WorkerTestHostProcessBoundaryLease`，重新造成本任務要解的 false positive。建議移除這三個孤兒定義，或至少加注記標明已被取代。

2. **固定 2 分鐘 lease timeout 在多類別合併同一 collection 後的餘裕未經量測**
   - `TestInfrastructure/WorkerTestHostProcessBoundaryCollection.cs:32`（`DefaultTimeout = TimeSpan.FromMinutes(2)`）
   - 目前同一個 `WorkerTestHostProcessBoundaryCollection` 涵蓋 `DedicatedGatewayProcessBoundaryTests`、`OfficialWorkerControlPlaneAdmissionTests`、`OfficialWorkerProfileExecutorTests`、`OfficialWorkerSoakAndPerformanceTests`（`GenerationCount = 6`、每代 128 次真實子程序請求的 soak 測試）四個類別的完整 fixture 生命週期；先取得 lease 的 test assembly 會持有到該 collection 全部測試跑完才 Dispose。若 Dynamics 與 ChurchReport 兩個 assembly 真的同時啟動（此 PR 要解決的正是這個情境），後到者必須在 2 分鐘內等到前者釋放，而前者要跑完的測試總量看起來很可能超過 2 分鐘。目前只有「單獨跑 targeted 命令」與「舊版分開跑的 full-run」證據，尚未看到兩個 assembly 真正並行時的實測耗時。建議在後續 fresh full gate 中特別量測两者平行執行下是否逼近或超過 2 分鐘，避免把合法忙碌誤判為 deadlock 而導致間歇性失敗。

## Info 🔵

1. **design.md 範圍描述與實際實作有落差**：`.trellis/tasks/08-12-process-boundary-cross-assembly-isolation/design.md:27-28` 的「範圍」只列出 `OfficialWorkerProfileExecutorTests`、`OfficialWorkerControlPlaneAdmissionTests`、`OfficialWorkerSoakAndPerformanceTests`、`FeatureDisabledDynamicsProcessBoundaryTests`，未提及 `DedicatedGatewayProcessBoundaryTests`。但實作額外把它（`SpeechMessage.Dynamics.Tests/DedicatedGatewayProcessBoundaryTests.cs:37`）也併入同一 collection，這是正確且必要的（該測試的零 worker 觀察清單含 `SpeechMessage.Dynamics.WorkerTestHost`，見同檔 49-55 行），只是文件未同步更新，建議之後補上避免範圍敘述漂移。

2. `WorkerTestHostProcessBoundaryLease.Acquire()`（`WorkerTestHostProcessBoundaryCollection.cs:104-115`）以兩個條件相近的 `catch (IOException) when (IsExpectedContention(...) && ...)` / `catch (IOException) when (IsExpectedContention(...))` 分流「繼續輪詢」與「逾時拋出」，邏輯正確、有對應契約測試覆蓋，純粹可讀性層面可以再精簡，但不影響正確性，僅供參考。

## 結論

未發現 Critical 等級問題；核心機制（worktree 分割、fail-closed 錯誤分類、單一 owner 生命週期、collection 覆蓋範圍）皆符合需求。建議在合併前處理上述兩項 Warning：清理孤兒 `CollectionDefinition`，並用一次雙 assembly 平行的 fresh full gate 驗證 2 分鐘 timeout 的實際餘裕。
