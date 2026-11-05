# Gemini UI & Architecture Review: P1-02 Process Lifecycle Review

## 1. 摘要 (Summary)
本次審查針對 P1-02 關於處理序生命週期管理（Process Lifecycle）、架構測試讀取器（`BoundedProcessReader` / `ArchitectureProjectReader` / `BoundedProcessRunner`）以及高並行 CRM 查詢快取測試（`CollectionQueryServiceAsyncTests`）的變更進行獨立程式碼審查。整體實作架構極為嚴謹，採用了 Fail-Closed、資源防呆、`Volatile` 發布與非同步 pipe 串流緩衝截斷保護。

以下依照審查要求針對六大核心問題逐一檢視並給予評級。

---

## 2. 審查問題逐一回答 (Review Questions Answered)

### Q1. 執行與排水期限、stdout/stderr 無上限記憶體防護、以及截斷 MSBuild JSON 拒絕機制
- **檔案與評估**：`tests-shared/ArchitectureTests/BoundedProcessRunner.cs` (Lines 29-82, 108-127), `ArchitectureProjectReader.cs` (Lines 116-141)
- **分析**：
  - `BoundedProcessRunner.Run` 支援單一整體驗證期限（`timeout`），透過 `Stopwatch` 計算剩餘時間，分別約束 `process.WaitForExit` 與 `WaitWithin(drain, ...)`。
  - `DrainAsync` 採用固定大小的 `char[4096]` 緩衝區搭配 `StringBuilder` 進行串流讀取，並根據 `maximumCapturedCharacters` 進行截斷控制（`truncated |= count < read`），避免記憶體隨著輸出無限膨脹。
  - 在 `ArchitectureProjectReader.Evaluate` 中明確檢查 `result.StandardOutputTruncated || result.StandardErrorTruncated`，若超出 1 MB 上限即拋出 `InvalidDataException`（`Read-only architecture evaluation exceeded its output limit.`），成功拒絕截斷的 MSBuild JSON。
- **結論**：符合要求。

### Q2. Timeout、程序樹終止、根程序收割、串流釋放與 reader 任務清理
- **檔案與評估**：`tests-shared/ArchitectureTests/BoundedProcessRunner.cs` (Lines 131-267)
- **分析**：
  - 逾時或失敗時呼叫 `StopAndReap`，優先使用 `process.Kill(entireProcessTree: true)` 終止整個程序樹，並透過 `process.WaitForExit` 確保根程序實際結束。代碼中妥善處理了 `InvalidOperationException` 與 `Win32Exception`（針對程序已自行結束的狀況）。
  - `Cleanup` 確保在發生例外時依序 Dispose `StandardOutput` 與 `StandardError` 串流，並等待 `drain` 任務在 `TerminationGracePeriod` 內完成。
  - 生產環境調用 `dotnet msbuild` 評估時，嚴格附加 `-maxcpucount:1` 與 `-nodeReuse:false`，且不執行任何建置目標（僅專案評估屬性與項目），符合規格。
- **結論**：符合要求。

### Q3. 清理失敗是否會遮蔽主要例外、滯留控制代碼或返回假成功
- **檔案與評估**：`tests-shared/ArchitectureTests/BoundedProcessRunner.cs` (Lines 83-101, 195-237)
- **分析**：
  - 若 `Run` 過程發生主要例外（`primaryFailure`），進入 `catch` 區塊呼叫 `Cleanup`。若 `Cleanup` 也發生例外，會透過 `AggregateException` 將 `primaryFailure` 與 `cleanupFailure` 同時包裝擲回，**絕不**會遮蔽主要例外或返回成功。
  - 串流與執行緒控制代碼透過 `using` 與 `Dispose` 確實釋放。
- **結論**：符合要求。

### Q4. 程式叫用是否無 Shell、無敏感輸出洩漏、引數與專案評估是否安全
- **檔案與評估**：`tests-shared/ArchitectureTests/BoundedProcessRunner.cs` (Lines 31-35), `ArchitectureProjectReader.cs` (Lines 96-110, 116-128)
- **分析**：
  - 強制檢查 `startInfo.UseShellExecute == false` 且必須重新導向標準輸出與錯誤，否則拋出 `ArgumentException`。
  - 專案路徑經過 `ValidateLocalPath` 嚴格檢查，禁止超出工作目錄（Root）以及拒絕 RePARSE Point（符號連結/接合點劫持），防止本地路徑高權限越權讀取。
  - 引數透過 `ArgumentList.Add` 安全傳遞，無字串串接拼接命令漏洞。
- **結論**：符合要求。

### Q5. `QueryFixture.LastQuery` 的安全發布與 64 執行緒並行測試之正確性
- **檔案與評估**：`ChurchReport.MemberInfo.Tests\Performance\CollectionQueryServiceAsyncTests.cs` (Lines 215-251)
- **分析**：
  - `QueryFixture.LastQuery` 內部使用 `Volatile.Read(ref lastQuery)` 與 `Volatile.Write(ref lastQuery, value)` 確保多執行緒記憶體能見度（Memory Visibility）。
  - `QueryFixture_LastQuery_ConcurrentCallsPublishCompleteQueryReference` 測試發起 64 個並行查詢，驗證回傳的 `completedQueries` 集合中必包含當前 `source.LastQuery` 所指向的執行個體，且不假設非決定論式的贏家（`Assert.Contains(completedQueries, query => ReferenceEquals(query, lastQuery))`），測試邏輯嚴謹合法。
- **結論**：符合要求。

### Q6. C# 註解完整性與繁體中文 XML 文件/生命週期規範
- **檔案與評估**：所有新建立與修改的 C# 檔案。
- **分析**：
  - 新增的 `BoundedProcessRunner.cs` 與 `ArchitectureProcessRunnerTests.cs` 均具備完整的繁體中文 XML 文件註解（`<summary>`, `<param>`, `<returns>`），且嚴格遵循專案的生命週期與錯誤處理規範。無多餘或無關的重構。
- **結論**：符合要求。

---

## 3. 審查發現 (Findings)

### Critical
- *(無，所有關鍵生命週期、安全性與執行緒安全檢查均已落實)*

### Warning
- *(無)*

### Info
- **Info-01 (`tests-shared/ArchitectureTests/BoundedProcessRunner.cs`)**:
  - 部分註解文字在某些編碼檢視工具下可能出現少數問號佔位符（例如檔案表頭說明行），建議確保所有原始碼檔案在 Git 設定中維持嚴格的 UTF-8 with BOM 或 UTF-8 without BOM 統一編碼，以維護跨平台顯示一致性。

---

## 4. 剩餘驗證差距 (Residual Verification Gaps)
1. 實際大型方案（包含數十個專案）在 CI 環境中同時發動多次並行 MSBuild 評估時的極端 CPU 飢餓情境，已由 `net10.0` 下 67 個架構測試全數通過間接驗證，但仍需在實際部署管線進行實機驗收確認。

## 5. 總結建議
- **RECOMMENDATION**: **PASS**
