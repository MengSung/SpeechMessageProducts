# P1-02 程序生命週期審查結果

> 審查範圍：`BoundedProcessRunner.cs`、`ArchitectureProjectReader.cs`、`ArchitectureProcessRunnerTests.cs`、`CollectionQueryServiceAsyncTests.cs`、`Test-ProgressPage.cjs`、`實作進度.html`（均對應 commit `55e7caa1`）。
> 說明：本次審查是在目前對話中直接讀取檔案與 `git show` 比對完成，並未透過外部 CCG 雙模型管線（Gemini／codeagent-wrapper）執行；若需要正式的雙模型複審紀錄，仍須另外跑那條管線並把本結果當作額外交叉比對依據。

## Critical 🔴
無。

## Warning 🟡
無。整體設計（單一到期時間基準、持續排空但有界保留、清理失敗與主因合併回報、`Volatile` 安全發布）在程式碼與測試中都能自洽驗證，沒有找到會造成誤判成功、資源洩漏或吞掉主要錯誤的路徑。

## Info 🟢

1. **`BoundedProcessRunner.cs:205`** — `Cleanup` 的例外篩選 `exception is InvalidOperationException or Win32Exception or AggregateException` 過寬：`StopAndReap` 實際上永遠把內部例外包成 `InvalidOperationException` 再丟出（見 180、185 行），不會直接丟出 `Win32Exception`／`AggregateException`。這兩個型別在此處是無法觸發的死碼，不影響正確性，但會讓讀者誤以為 `StopAndReap` 可能直接外洩這些型別。建議（非必要）：篩選收斂為 `InvalidOperationException` 即可，不需調整行為。

2. **`BoundedProcessRunner.cs:131-187`（`StopAndReap`）與 `:210-227`（`Cleanup`）** — 殘留驗證缺口：當「輸出排空期限」分支觸發時（`BoundedProcessRunner.cs:68-72`），根程序此時已經 `HasExited`，`StopAndReap` 內的 `if (!process.HasExited) Kill(entireProcessTree: true)` 整段會被跳過，實際解除排空阻塞的手段只剩 `Cleanup` 對 `StandardOutput`/`StandardError` 的 `Dispose()`。也就是說，若根程序已自然結束但仍有尚未被回收的子代程序持有管線寫入端，本函式不會主動終止該子代，只會關閉本端讀取管線。這與類別頂端註解「此 runner 不可擴用到會自行建立子程序的工作負載」及目前唯一正式呼叫者（`dotnet msbuild` 搭配 `-nodeReuse:false -maxcpucount:1`、純評估不跑 target）的契約一致，屬已知且已記載的限制，非隱藏缺陷。僅提醒：若未來新增會自行產生子程序的呼叫者，此路徑不保證終止孤兒子代。

3. **`ArchitectureProcessRunnerTests.cs:63`** — `CreatePowerShellStartInfo` 用 `ProcessStartInfo(string, string)` 的舊式 `Arguments` 字串而非 `ArgumentList`，與 `ArchitectureProjectReader.cs:118-127` 一貫使用 `ArgumentList` 的風格不一致。目前安全，因為注入的只有固定旗標與 Base64（`A-Za-z0-9+/=`，不含殼層特殊字元），不構成注入風險；純屬風格差異，不要求修改。

4. **`CollectionQueryServiceAsyncTests.cs:216-235`（`QueryFixture_LastQuery_ConcurrentCallsPublishCompleteQueryReference`）** — 測試本身邏輯正確：64 個不同 query 平行呼叫後只斷言「最終值是其中一個完整已發布的 query 參考」，不斷言特定勝出者，符合「不可斷言不確定的贏家」要求；`Volatile.Read`/`Volatile.Write`（`CollectionQueryServiceAsyncTests.cs:247-251`）是修正可見性問題的標準做法。殘留驗證缺口：這類平行壓力測試只能提高重現機率，無法在單次通過時形式化證明「無論 CPU 記憶體模型都不會讀到過期值」；目前本機 64 路並行、16/16 全綠是經驗證據，但仍建議日後若 CI 環境換成較弱記憶體序平台時重跑確認。

## 殘留驗證缺口總結
- 未執行實際的 dotnet build/test（依指示不執行會對外部服務或建置環境造成影響的命令），本次審查純粹以人工讀碼 + 既有本機證據（67/67、2/2 故障注入、16/16、4/4）交叉核對；邏輯推演未發現與該證據矛盾之處。
- 未透過正式 CCG 雙模型（Gemini＋Claude）管線重新執行；本結果可作為該管線跑完後的額外交叉驗證，但不能取代它。
- 第 2 點提到的「根程序已退出但子代仍占用管線」情境在目前程式碼中沒有自動化測試覆蓋（現有兩個 `ArchitectureProcessRunnerTests` 案例涵蓋的是逾時未退出、以及正常退出但輸出超量兩種情境，未直接模擬「根已退出、孤兒子代持有管線」）；若要完全閉環，可考慮之後補一個此情境的注入測試，但非本次程式碼缺陷，純屬覆蓋率建議。

---
SESSION_ID: 03976414-1cee-445f-a009-25b005a0dfe3
