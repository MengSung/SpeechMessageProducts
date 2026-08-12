# 跨程序集 WorkerTestHost 程序隔離

## 目標

消除不同 xUnit 測試程序集同時執行時，`WorkerTestHost` 被錯誤歸因為 ChurchReport disabled feature 所啟動的 false-positive process-boundary failure；同時保留 disabled ChurchReport 路徑對「自身不得建立 Gateway、CRM Worker 或 WorkerTestHost」的真實安全斷言。

## 已確認事實

- `FeatureDisabledDynamicsProcessBoundaryTests` 單獨執行通過。
- 當 `OfficialWorkerSoakAndPerformanceTests` 在另一個 testhost 持有 `SpeechMessage.Dynamics.WorkerTestHost` 時，同一 ChurchReport 測試穩定失敗，並列出另一 testhost 建立的 PID。
- 既有 `[Collection(..., DisableParallelization = true)]` 僅能序列化同一測試程序集，不能跨 `SpeechMessage.Dynamics.Tests` 與 `ChurchReport.MemberInfo.Tests` 生效。
- 修正不得建立 CRM、Gateway、Official Worker、CE I/O、feature flag 或產品流量切換；它只處理本機測試程序邊界。

## 需求

1. 所有會建立 `WorkerTestHost` 的 Dynamics test class 與 ChurchReport disabled boundary test 必須共用同一個跨程序集、同一使用者 session 的本機互斥邊界。
2. 取得邊界必須有明確上限，逾時必須 fail closed；非預期例外不得被吞掉。
3. 邊界的唯一 owner 必須在 fixture disposal 時釋放檔案控制碼；testhost crash／abort 後由 OS 回收控制碼，不能永久毒化後續測試。
4. 互斥只能序列化 process-boundary 測試，不得關閉整個 solution 的平行測試，也不得放寬 `GetNewDynamicsBoundaryProcesses`、listener 或 cleanup assertion。
5. 新增可重現並驗證「worker test 與 disabled test 並行」的自動化證據，並檢查處理結束後沒有 `WorkerTestHost` 殘留。

## 驗收條件

- [x] 單獨 ChurchReport disabled-boundary test 通過，且原 process／listener assertion 不變。
- [x] 受控並行 worker/ChurchReport 測試改為序列化並通過，未隱藏真正產生的 worker；ChurchReport 在 lease 釋放後通過且 `WorkerProcessesAfter=0`。
- [x] Dynamics WorkerTestHost test collection 維持測試內序列化，且跨程序集 lease 可在正常及 abort 路徑釋放；unit contract 覆蓋 contention、bounded timeout、dispose release、非 contention I/O fail-closed 與 worktree partition。
- [x] 相關 targeted tests、兩個完整 test project、完整 solution test、Release build、UTF-8 無 BOM、CRLF、`git diff --check` 與 scope check 通過；詳見 `check-progress-2026-08-12.md`。外部雙模型審查未在 45 秒內完成，已明確降級而非偽稱完成。

## 非範圍

- 修改 ChurchReport 產品啟動、Dynamics transport、CE fixture、Credential、feature gate 或 Gateway 部署。
- 將 P7.2 Slice C 的 CE no-go 重新分類或重試。
