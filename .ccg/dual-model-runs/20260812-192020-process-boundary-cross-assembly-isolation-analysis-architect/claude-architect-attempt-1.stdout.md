## 分析結論(去識別化)

### 1. 整體評估

設計核心正確:用 OS 檔案控制代碼(`FileShare.None` + `%TEMP%`固定路徑 + bounded polling)取代具名 `Mutex`,避免 xUnit fixture 建構/釋放發生在不同 thread 時的 thread-affinity 例外,且利用「行程結束後 OS 自動釋放 handle」達成 abort-safe 語意。用 source-link 而非套件相依共享同一份 fixture 原始碼,可保證兩個 test assembly 使用同一 lock 識別(路徑/檔名)與同一組行為,這點是本設計能真正跨程序集生效的關鍵前提,思路正確。

但實際落地(依現有 RED 測試 `WorkerTestHostProcessBoundaryLeaseTests.cs` 與 `design.md`)仍有三個尚未在文件中處理的落差,足以影響「最小必要測試集合」判斷與長期穩定性。

---

### 2. Findings

**Critical — 固定 lock 檔名未依 checkout/worktree 命名空間化**
- 位置:`design.md` 機制章節,`%TEMP%/speechmessage-worker-testhost-process-boundary-v1.lock`
- 問題:`%TEMP%` 在 Windows 是每個使用者帳號共用,不是每個 repository checkout 各自獨立。目前的路徑是本機環境下多個 git worktree(即本任務所在的並行分支結構)在同一使用者 session 底下同時執行測試時會共用的全域資源。若兩個彼此無關的 worktree/分支同時各自跑 `SpeechMessage.Dynamics.Tests` 與 `ChurchReport.MemberInfo.Tests`,它們會爭奪同一把 lease,可能導致其中一個 worktree 的測試在 bounded timeout(草案中 60 秒)後拋出 `TimeoutException` 而失敗——這是把「跨程序集誤判」問題換成「跨 worktree/跨分支的偽陽性逾時失敗」,而非真正消除偽陽性。
- 建議:lock 檔名應以穩定但不含敏感資料的方式納入 checkout 識別(例如以 repository root 絕對路徑做穩定雜湊),確保 lease 範圍限定在「同一次 solution 執行」而非「同一台機器的同一使用者」。

**Warning — `DedicatedGatewayProcessBoundaryTests` 具備相同的「零 WorkerTestHost」契約卻未被納入共享 collection**
- 位置:`SpeechMessage.Dynamics.Tests/DedicatedGatewayProcessBoundaryTests.cs`(獨立的 `DedicatedGatewayProcessBoundaryCollection`,`DisableParallelization = true`)
- 問題:此類別與 `FeatureDisabledDynamicsProcessBoundaryTests` 屬同一種測試模式——啟動真實子程序後斷言「未建立 Gateway/CRM Worker/WorkerTestHost」。它與會建立 `WorkerTestHost` 的 `OfficialWorkerSoakAndPerformanceTests` 同屬 `SpeechMessage.Dynamics.Tests` 單一 assembly,但兩者是不同的 xUnit collection。xUnit 預設「跨 collection 平行、同 collection 序列」,因此這兩個 collection今天本來就可能在同一組 `dotnet test` 呼叫中並行執行,存在與本任務描述完全相同的偽陽性風險,只是發生在單一 assembly 內而非跨 assembly。`design.md`/`prd.md` 的納入範圍都沒有提到它。
- 建議:若要讓「最小必要測試集合」真正完整,應將 `DedicatedGatewayProcessBoundaryTests` 一併納入 `WorkerTestHostProcessBoundaryCollection`,否則此漏洞會在修復後依然潛伏,且更難被目前受控並行的重現案例偵測到(因為重現目前鎖定在 ChurchReport vs Dynamics soak 之間)。

**Warning — lease 生命週期粒度(collection-fixture vs class-fixture)未在設計中明確,影響序列化範圍**
- 位置:`design.md`「機制」圖示(`test class starts -> xUnit collection fixture -> ... -> fixture Dispose`)
- 問題:文字同時使用「class-lifetime」與「collection fixture」兩種說法。若採 `ICollectionFixture`(每個 collection 一個實例,涵蓋該 collection 內所有類別的整個執行期間),則每個 test assembly 在取得 lease 後會持有到該 assembly 內「所有」被歸入此 collection 的類別(soak/perf 測試在內)全部跑完為止;soak 測試若耗時較長,ChurchReport 的 disabled-boundary 測試將被迫等待整段 soak 期間才能取得 lease,序列化成本可能遠高於單一測試類別所需。若改採 `IClassFixture`(在同一 collection 內,每個類別各自取得/釋放),仍可靠 xUnit「同 collection 內序列執行」的保證維持正確性,但可把跨程序鎖的持有時間縮小到單一類別範圍,降低不必要的跨程序阻塞。
- 建議:設計文件應明確指定採用哪一種 fixture 粒度,並在驗證策略中量測實際序列化造成的執行時間增量,而非僅定性描述「影響極小」。

**Info — DeleteOnClose 與 bounded polling 的交互行為未明確驗證**
- 位置:機制設計(是否使用 `FileOptions.DeleteOnClose`未在 `design.md` 明確寫出,僅 RED 測試以黑箱方式呼叫 factory)
- 說明:若採用 `DeleteOnClose`,持有者釋放時作業系統會刪除該檔案;下一個等待者的 `OpenOrCreate` 重新建立檔案是預期行為,但需注意「檔案被標記為刪除中」與「一般共用違規」在 Win32 底層有不同的錯誤路徑,兩者都應轉為可重試的 `IOException`/`UnauthorizedAccessException` 才能被目前的 bounded polling 迴圈正確涵蓋。現有 RED 測試(`Concurrent_owner_is_rejected_after_the_bounded_process_boundary_deadline`、`Disposed_owner_releases_the_process_boundary_for_the_next_testhost`)已涵蓋核心排他與釋放路徑,但沒有專門驗證「delete-on-close 期間的競爭」這個子案例,建議在正式實作前補一個等價測試,以免只靠人工判讀是否等價。

**Info — 反射式 factory 呼叫(`AcquireForTesting` via `BindingFlags.NonPublic`)屬合理的 TDD RED 手法,無安全疑慮**
- 位置:`WorkerTestHostProcessBoundaryLeaseTests.cs`
- 說明:此測試刻意先讓型別不存在以形成 RED,是正常 TDD 流程,不涉及 CRM/Gateway/credential,鎖檔路徑使用每案例唯一的 `Guid`,`finally` 有清理,不影響去識別化要求。無需修正。

---

### 3. 最小必要測試集合(依現況修正後的建議)

以下類別必須共用同一個 `WorkerTestHostProcessBoundaryCollection`(依實際觀察到的程式碼現況,而非文件遺漏):
1. `SpeechMessage.Dynamics.Tests.OfficialWorkerSoakAndPerformanceTests`(建立 WorkerTestHost)
2. `SpeechMessage.Dynamics.Tests.OfficialWorkerProfileExecutorTests`(建立 WorkerTestHost,目前無任何 `[Collection]`)
3. `SpeechMessage.Dynamics.Tests.OfficialWorkerControlPlaneAdmissionTests`(建立 WorkerTestHost,目前無任何 `[Collection]`)
4. `SpeechMessage.Dynamics.Tests.DedicatedGatewayProcessBoundaryTests`(要求零 WorkerTestHost,現有設計遺漏,見上方 Warning)
5. `ChurchReport.MemberInfo.Tests.FeatureDisabledDynamicsProcessBoundaryTests`(要求零 WorkerTestHost)

其餘不建立/不斷言 WorkerTestHost 的測試類別不應加入,以免不必要地擴大序列化範圍。

---

### 4. 是否能正確避免跨程序集 false positive?

在補上「Critical:lock 檔名命名空間化」與「Warning:`DedicatedGatewayProcessBoundaryTests` 納入」兩項之前,設計對「本任務描述的特定重現案例」(ChurchReport disabled test vs Dynamics soak test)是有效的;但作為通用的「跨程序集/跨 worktree 隔離基礎設施」尚不完整,存在會把原偽陽性換成新型態偽陽性(逾時失敗)的具體路徑。建議在實作前於 `design.md` 補齊上述兩點,並在驗證策略中新增「多 worktree 並行執行」與「DedicatedGatewayProcessBoundaryTests 併入後的完整並行重現」兩個檢查項。

---
SESSION_ID: c37fb9aa-f43a-4763-92bd-235f2a2684a1
