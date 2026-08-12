# 跨程序集 WorkerTestHost 程序隔離審查與驗證

## 狀態

- CCG architecture analysis：Gemini 與 Claude 都在原定 45 秒外完成；其 findings 僅作為後續
  唯讀驗證，不宣稱為期限內雙模型成功。
- CCG reviewer：透過 `Start-CcgDualModelRun.ps1` 啟動，run ID 為
  `20260812-194248-process-boundary-cross-assembly-isolation-review-reviewer`，runner timeout 設為 45 秒。
  runner 未在期限自行結束；宿主工具約 59 秒回報 timeout 後立即終止殘留 chain。期限內沒有可用
  report，已標記「雙模型未完成」，不重試等待。
- 本機 review：確認 collection 僅 source-link 至 test assembly、沒有 production reference；
  `FileStream` 是唯一 owner，無 static handle；source hash partition 不洩露原始 worktree path；
  非 contention I/O 不會轉為 timeout；ChurchReport observer 的 process/listener/cleanup assertion
  未被放寬。

## 驗證結果

- focused Dynamics：7 passed。
- focused ChurchReport：1 passed。
- complete solution test：ChurchReport 528 passed / 14 explicit skips；Dynamics 664 passed / 7 explicit skips；
  0 failures。
- Release build：0 warnings、0 errors。
- full-suite 後無 `SpeechMessage.Dynamics.WorkerTestHost` process。
- 受控兩 testhost 重現：Dynamics worker 已觀察到後才啟動 ChurchReport；兩者 exit code 都是 0，
  ChurchReport elapsed 10682 ms，最後 worker count 為 0。

## 結論

沒有本機發現的 Critical 或 Warning。外部雙模型 review 未在 45 秒期限內完成，故交付狀態是
「本機驗證完成＋雙模型未完成」，不是完整雙模型審查通過。
