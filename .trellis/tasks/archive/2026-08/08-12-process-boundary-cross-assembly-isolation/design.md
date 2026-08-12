# 跨程序集 WorkerTestHost 程序隔離設計

## 邊界與原因

`WorkerTestHost` 是本機 OS process，名稱與 listener snapshot 都是同一使用者 session 的共享資源；xUnit 的 collection 卻只在單一 test assembly 生效。ChurchReport 測試用 process baseline 偵測新 worker 是正確的安全契約，但若另一程序集在 baseline 後啟動合法的 WorkerTestHost，名稱式偵測無法判定 ownership，因而產生 false positive。

不以降低 process assertion 修正問題。改由所有會建立 WorkerTestHost 的 test class 與「要求零 WorkerTestHost」的 ChurchReport class，在建立 baseline 前取得同一個 test-only interprocess lease。lease 在 class collection fixture 的完整生命週期內持有，因此在被保護的程序觀察期不會有另一程序集建立同名 test worker。

## 機制

新增一個以 source link 編譯進兩個 test assembly 的 `WorkerTestHostProcessBoundaryCollection`：

```text
test class starts
  -> xUnit collection fixture
  -> calculate SHA-256 partition from the canonical solution root
  -> open %TEMP%/speechmessage-worker-testhost-process-boundary-v1-{partition}.lock
     with FileShare.None and bounded polling
  -> run class tests / create or assert zero WorkerTestHost
  -> fixture Dispose closes stream in finally-equivalent lifecycle
```

選擇檔案控制碼而非 named `Mutex`：檔案控制碼不是 thread-affine，xUnit fixture 的建立與 disposal 即使在不同 scheduler thread 仍可釋放；testhost crash 時 OS 自動關閉控制碼。lock file 是無內容的 temporary synchronization artifact；相同 worktree 由 canonical solution root 的 SHA-256 前綴取得同一個 partition，不同 checkout 不會因 `%TEMP%` 的固定檔名互相阻塞。檔名不含原始 root path、user、profile、endpoint、credential、CRM data 或測試 payload。

## 範圍

- 新增 shared test-only collection/fixture source；它不應進入 production assembly。
- 將 `OfficialWorkerProfileExecutorTests`、`OfficialWorkerControlPlaneAdmissionTests`、`OfficialWorkerSoakAndPerformanceTests` 及 `FeatureDisabledDynamicsProcessBoundaryTests` 放入 collection。
- 兩個 test `.csproj` link 同一 shared source，保證固定 lock identity 與相同行為。
- 為 shared helper 增加 unit contract，測試 contention、timeout、disposal release 與 worktree-hashed path partition；最小測試可使用暫存目錄的可注入 lock path，正式 fixture 一律由 solution-root partition 衍生安全的 temp path。

## 失敗與 cleanup

- `FileShare.None` contention 時只做 bounded `IOException` polling；時間到拋出固定無敏感資料的 `TimeoutException`，不繼續執行可能污染 assertion 的 class。
- 開啟後所有 ownership 由 fixture 的 `FileStream` 唯一持有；fixture disposal 保證 `Dispose`。若 testhost 被 abort，OS 釋放 handle，下一次的 bounded acquisition 可復原。
- 不儲存 static stream、test result、process identity、principal、cookie 或任何可變 request state。

## 驗證策略

1. 先以 failing tests 證明 helper 能排他並在 release 後讓等待者進入。
2. 套用 collection，重跑已重現的 worker-soak 與 ChurchReport disabled test 並行案例。
3. 檢查 ChurchReport 的 worker/process/listener assertion 不變，且兩個 test project／solution 完整通過。
