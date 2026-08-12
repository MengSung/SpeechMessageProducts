# 跨程序集 WorkerTestHost 程序隔離檢查紀錄

## 實作結果

- 根因是 xUnit collection 僅能序列化同一 test assembly；當 Dynamics soak test 的
  `WorkerTestHost` 與 ChurchReport disabled-boundary test 於不同 testhost 交錯時，後者的
  baseline 正確看見新程序，但無法辨識它屬於另一程序集，因此出現 false positive。
- 修正沒有降低 ChurchReport 的 process、listener 或 cleanup assertion。所有會建立
  `WorkerTestHost` 的 Dynamics test class，以及 ChurchReport 的零-worker observer class，
  都在 class lifecycle 內取得同一個 source-linked test-only `FileShare.None` lease。
- lock path 由 canonical solution root 的 SHA-256 前 16 位組成 partition；同一 worktree 的
  testhost 互斥，不同 checkout 不會因 `%TEMP%` 的固定檔名互相阻塞。temporary artifact 無內容，
  不含 root path、使用者、profile、endpoint、credential、CRM payload 或 session data。
- 唯一資源 owner 是 fixture 的 `FileStream`；正常 disposal 關閉 handle，testhost abort 時由 OS
  回收。只有 Win32 sharing/lock violation（32/33）可在 2 分鐘固定期限內輪詢；其他 I/O fault
  直接 fail closed。

## 本機驗證

| 指令／檢查 | 結果 |
| --- | --- |
| `dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WorkerTestHostProcessBoundaryLeaseTests\|FullyQualifiedName~OfficialWorkerSoakAndPerformanceTests\|FullyQualifiedName~DedicatedGatewayProcessBoundaryTests"` | 7 passed、0 failed、0 skipped。 |
| `dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~FeatureDisabledDynamicsProcessBoundaryTests"` | 1 passed、0 failed、0 skipped。 |
| `dotnet test .\SpeechMessageProducts.sln -c Release --no-restore --nologo -v minimal` | ChurchReport 528 passed、14 explicitly gated live/reparse skipped；Dynamics 664 passed、7 explicitly gated live SQL skipped；無 failed test。 |
| `dotnet build .\SpeechMessageProducts.sln -c Release --no-restore --nologo -v minimal` | 0 warnings、0 errors。 |
| full-suite 後 `Get-Process SpeechMessage.Dynamics.WorkerTestHost` | 無殘留程序。 |
| lease unit contract | 覆蓋 contention timeout、dispose release、non-contention I/O 原樣 fail-closed、同 worktree stable / 跨 worktree partition。 |

受控跨程序集重現使用兩個獨立 PowerShell testhost：先執行 Dynamics
`WorkerSoak_repeated_package01_recycle_returns_all_owners_to_zero_without_unbounded_trends`，確認它已建立
`WorkerTestHost` 後才啟動 ChurchReport disabled-boundary test。結果為
`WORKER_OBSERVED=True`、`DYNAMICS_EXIT=0`、`CHURCH_EXIT=0`、`CHURCH_ELAPSED_MS=10682`、
`WORKER_PROCESSES_AFTER=0`。ChurchReport 因 shared lease 在 worker lifecycle 完成後才取得 process
baseline；它沒有忽略真正的 worker，而是避免把另一程序集的 test-owned worker 錯當成自身 startup 所建立。

## 審查狀態

- 已依 CCG self-healing runner 啟動 reviewer run
  `20260812-194248-process-boundary-cross-assembly-isolation-review-reviewer`，並將 runner timeout 設為
  45 秒。runner 沒有在該期限自行結束；宿主工具於約 59 秒回報 timeout 後，立即終止其殘留的
  runner／wrapper chain，沒有再等待或重試。
- 期限內沒有可用 Gemini/Claude report，因此記錄「雙模型未完成」。此狀態不是 completed
  dual-model review，也沒有用 timeout 偽裝為正面審查結果。
- 先前 architecture analysis run
  `20260812-192020-process-boundary-cross-assembly-isolation-analysis-architect` 的期限後 findings
  已作為唯讀意見驗證；其中「固定 TEMP 檔名會跨 worktree contention」已採納並改為 hash partition。

## 範圍與不變項

- 沒有 CE read/write、fixture、feature flag、ChurchReport 流量、Official Worker 實機、CE 8.2 或
  雲端部署操作。
- 未重試 P7.2 Slice C historical no-go，也沒有接觸其 nonce、ledger、fixture 或 descriptor。
- 本 task 的 spec feedback 已寫入既有 Dynamics Gateway routing contract，說明跨程序集 testhost
  process-boundary test 必須使用 worktree-partitioned bounded lease，而不能依賴 assembly-local collection。
