# Research: test-surface-audit

- Query: 盤點可驗證 ChurchReport「伺服器衍生、不可變授權邊界」的既有測試專案、測試縫隙與生命週期風險；聚焦 A/B 交錯、惡意 locator、授權前零 I/O、取消／故障清理、預設停用及無 fallback。
- Scope: internal
- Date: 2026-08-14

## Findings

### 測試專案與建議分層

| 專案／測試面 | 可證明的契約 | 建議用途 |
| --- | --- | --- |
| `ChurchReport.MemberInfo.Tests` | 已是 net10.0、xUnit、FluentAssertions，並直接參考 ChurchReport 專案。 | 新 shared-boundary 的主要單元與 controller contract 測試位置。見 `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj:3-28`。 |
| service fake／deferred Task | 真正的 A/B 交錯、profile／locator／取消 token 不交叉。 | 最適合 immutable scope resolver + typed dispatch 的行為測試。 |
| controller source-contract | 固定 gate、server scope、parse、目標授權、client dispatch 的相對順序，並禁止 legacy SDK／cache／retry 字樣。 | 保護 routing 邊界，避免往後重接 Session、`ToolUtility` 或 fallback。 |
| process-boundary | 停用旗標下不啟動 Gateway／worker／listener，且關機後回到 baseline。 | 僅作慢速整合測試；不應取代純單元層的所有拒絕案例。 |

### 可直接復用的測試模式

1. **A/B 真正交錯與不可變結果：**
   `Package02MemberInfoPresentRecordReadServiceTests` 以兩個 `TaskCompletionSource` 延後回應，先後反轉完成，仍驗證 A/B 的 profile、contact、結果集合與 cancellation token 各自獨立（`ChurchReport.MemberInfo.Tests/Services/Package02MemberInfoPresentRecordReadServiceTests.cs:93-124`）。新 boundary 測試應以不同的 validated subject、profile alias、generation、authorization scope 與 locator marker 建立兩個 scope；在 B 先完成後，雙方 response、dispatch request、診斷分類均不得含對方 marker。

2. **避免「Task.WhenAll 其實未並行」的假陽性：**
   `DownloadIntegrateDataPresentRecordIsolationTests` 使用 `Barrier(2)` 加 `Task.Run` 強制兩個操作都進入 fake I/O 才放行（`ChurchReport.MemberInfo.Tests/WebServiceConnector/DownloadIntegrateDataPresentRecordIsolationTests.cs:92-117`）；fake 於 `RetrieveMultiple` 以五秒上限等待，並拒絕所有未預期 SDK 呼叫（同檔 `:223-301`）。這是 shared resolver／dispatcher 的最佳競態縫隙：對 A/B 同時解析、授權與 dispatch，fake 應記錄每一步並斷言 invalid／unauthorized 請求的 I/O 計數始終為 0。

3. **較輕量的 request-local defensive-copy 範例：**
   `FeeEditorReadServiceTests` 已驗證 A/B 結果、rows 與 row instance 不共享（`ChurchReport.MemberInfo.Tests/Services/FeeEditorReadServiceTests.cs:135-163`），並以 request count 與 observed cancellation token 偵測 retry／fallback（`:170-213`）。可沿用 fake 結構，但此案例的回應立即完成，不能單獨作為 race 證明；應搭配上項的 deferred／Barrier 模式。

4. **惡意／未授權 locator：**
   `FeeEditorLessonAccessResolverTests` 對未載入、null、非 GUID、重複 server snapshot 均 fail closed 且回傳空 allowlist（`ChurchReport.MemberInfo.Tests/Services/FeeEditorLessonAccessResolverTests.cs:54-80`），並驗證 browser locator 必須命中 server allowlist（`:88-99`）。新 shared boundary 至少應追加：缺少 scope、過期／ambiguous scope、空白與 malformed locator、合法但未授權 locator、caller 偽造 profile／tenant／credential／organization；每一種皆須在 cache、client、connector 或任何 outbound I/O 前固定拒絕。

5. **授權前零 I/O 的 action 順序：**
   `MemberInfoControllerPackage03FullContactImageContractTests` 已以 index assertions 固定順序為 gate → `EnsureCorrectUserData` → scope → GUID parse → `CanViewContact` → typed client（`ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPackage03FullContactImageContractTests.cs:33-70`）。`FeeManagementControllerFeeEditorReadContractTests` 另證明 gate 在 login、session snapshot、locator、client 之前（`:28-49`），且 server snapshot 在 parse／dispatch 之前、沒有 legacy lesson loader／CRM I/O（`:57-78`）。shared boundary 的 controller tests 應採同一模式，並將 I/O fake 的零呼叫斷言作為行為層補強，避免 source-only 測試遺漏間接配置。

6. **無 fallback／retry 與取消傳遞：**
   present-record typed action 明確禁止 `ToolUtility`、SDK、`catch` 與 retry，且 `CanViewContact` 在 client factory 之前（`ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPresentRecordContractTests.cs:70-98`）。其 service 則要求 cancellation 原樣拋出且 invocation count 為一（`ChurchReport.MemberInfo.Tests/Services/Package02MemberInfoPresentRecordReadServiceTests.cs:131-144`）。新測試應分別注入 cancellation-before-dispatch、cancellation-after-dispatch 與 typed fault；前者 I/O=0，後兩者 I/O=1、無 retry／legacy fallback，且只由既有 lease／client owner 決定是否淘汰／dispose。

7. **預設停用與 factory 無資源配置：**
   `AuthenticationContactReadBootstrapTests` 證明空白設定的 gate=false 直接回傳 null（`ChurchReport.MemberInfo.Tests/AuthenticationContactReadBootstrapTests.cs:37-47`），並以 method slice 確認 gate／`return null` 位於 options、profile、injected client、executor 之前（`:106-128`）。`DonationDynamicsAccessBootstrapPresentRecordContractTests` 同樣鎖定雙 gate 先於 `BindOptions` 與 host 解析（`ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapPresentRecordContractTests.cs:30-47`），並檢查兩份 checked-in appsettings 均為 false（`:55-61`）。這是 shared integration seam 的推薦 baseline：未明確啟用時不可解析 profile、Session、host、client、cache 或 connector，也不可 request-time legacy fallback。

8. **實際 host 的 disabled-by-default 證據：**
   `FeatureDisabledDynamicsProcessBoundaryTests` 啟動 ChurchReport 子程序，只呼叫 `/health`，確認 probe 無連線、無 Dynamics listener／新 process，並在 finally 與後置 assertion 驗證 port／process baseline（`ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs:54-109`）。若 shared gate 的停用路徑會影響 hosted service，可在此模式增加一個 focused test；但 locator／授權拒絕應留在不啟 host 的快速測試。

### 資源生命週期與 test-host 風險

- process-boundary test 已以 xUnit collection 禁止平行執行（`ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs:21`）；其 shared fixture 同樣標示 `DisableParallelization = true` 並持有 OS lock lease（`TestInfrastructure/WorkerTestHostProcessBoundaryCollection.cs:13-14`）。新 subprocess test 必須加入此 collection，且不可共用固定 port、PID 或 Gateway probe。
- `FeatureDisabledDynamicsProcessBoundaryTests` 的 probe owner 持有 listener、CTS、accept task 與可能的 client；其 disposal 順序是 cancel → stop listener → await accept task → dispose client／CTS（`ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs:466-516`）。複用時一定以 `try/finally`／`await using` 包住 host/probe，並保留 shutdown 後 listener/process baseline assertions。
- `DefaultHttpContext` 不會自行觸發真正伺服器的 `OnCompleted` callbacks；既有 session coordinator 測試也明確指出必須以自訂 response feature 補 server lifecycle（`ChurchReport.MemberInfo.Tests/SessionLifecycle/SessionScopedResourceDisposalCoordinatorTests.cs:1044-1051`）。shared boundary 若註冊 callback、lease 或 cancellation registration，必須測試 callback drain、取消與 fault 後的 resource counter 回到 baseline，而不能只 new `DefaultHttpContext`。
- `AuthenticationSessionResourceDrainTests` 會暫時改 process-wide current directory 以初始化 legacy type（`ChurchReport.MemberInfo.Tests/SessionLifecycle/AuthenticationSessionResourceDrainTests.cs:206-235`）。shared auth test 不應依賴或模仿此手法；若必須觸及 legacy session，使用既有 `SessionLifecycleCollection`，並保證還原 global state。
- A/B fake 的 collection／dictionary 必須是單一測試所有；若測試從同步啟動改為真正多執行緒 dispatch，避免未同步 `Dictionary` 成為測試自身 race。優先沿用 Barrier fake 或使用 `ConcurrentDictionary`。

### 相關規格

- `.trellis/spec/backend/cross-user-isolation-and-performance.md`：要求在 cache／allocation／I/O 前授權、A/B 交錯、取消／故障淘汰及 baseline 回復（第 3、4、6 節）。
- `.trellis/spec/guides/cross-user-isolation-and-performance-review.md`：要求逐一標示 owner／最大生命週期，並在核准前進行 interleaved A/B 與 lifecycle 驗證。
- `.trellis/tasks/08-14-p7-server-derived-authorization-boundary/prd.md`、`design.md`、`implement.md`：本任務的 immutable server-derived scope、fail-closed、無 legacy fallback 與 TDD 要求。

### Files found

- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` — ChurchReport 專用 net10.0 測試專案與相依性。
- `ChurchReport.MemberInfo.Tests/Services/Package02MemberInfoPresentRecordReadServiceTests.cs` — deferred A/B profile／token isolation 與 cancellation 無 retry。
- `ChurchReport.MemberInfo.Tests/WebServiceConnector/DownloadIntegrateDataPresentRecordIsolationTests.cs` — Barrier 強制並行、marker fake、borrowed-service ownership。
- `ChurchReport.MemberInfo.Tests/Services/FeeEditorLessonAccessResolverTests.cs` — server snapshot allowlist、ambiguous 與 unauthorized locator 拒絕。
- `ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPackage03FullContactImageContractTests.cs` — gate 到 typed dispatch 的 source-order／no-fallback contract。
- `ChurchReport.MemberInfo.Tests/Controllers/FeeManagementControllerFeeEditorReadContractTests.cs` — gate／snapshot／locator／dispatch 的無 I/O 順序。
- `ChurchReport.MemberInfo.Tests/AuthenticationContactReadBootstrapTests.cs` — false gate 早退與 factory order。
- `ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs` — 實際 apphost 的 disabled process／listener baseline。
- `TestInfrastructure/WorkerTestHostProcessBoundaryCollection.cs` — subprocess test 的跨程序互斥 lease。
- `ChurchReport.MemberInfo.Tests/SessionLifecycle/SessionScopedResourceDisposalCoordinatorTests.cs` — `OnCompleted` lifecycle 的 test-host limitation 與 response-feature seam。

## Caveats / Not Found

- 本次只做本機唯讀盤點；依指示未進行 CE、Gateway、HTTP 或外部網路操作。無外部參考資料。
- 在 `ChurchReport.MemberInfo.Tests`、`ChurchReport.Tests` 與 `TestInfrastructure` 未找到 `WebApplicationFactory`、`TestServer` 或 `Microsoft.AspNetCore.Mvc.Testing` 的現成 host fixture。真正 HTTP pipeline 授權整合測試目前應以 subprocess pattern，或新增受控的 in-process fixture；不可誤把 `DefaultHttpContext` 當成完整 server lifecycle。
- 既有多數 controller tests 是 source-contract；它們能防止可見順序／依賴回歸，但不足以證明間接 I/O 為零。因此 shared boundary 必須同時有 injected recorder/fake 的行為測試，並以 A/B barrier 實證。
- `task.py current --source` 在此 subagent session 回報 `(none)`；本報告依父代理明確指定的 task path 寫入，未變更 task 狀態或其他紀錄。
