# 跨程序集 WorkerTestHost 程序隔離實作計畫

1. 建立 shared test-only fixture 的 RED tests：排他、bounded timeout、dispose release、沒有 static retained handle。
2. 執行 targeted test，確認在 helper 不存在或行為未實作時以預期原因失敗。
3. 實作 bounded `FileShare.None` lease，加入完整繁體中文 lifecycle／isolation 文件。
4. 將 shared source link 到兩個 test project，並為所有 WorkerTestHost producer class 與 ChurchReport disabled-boundary class 套用同一 xUnit collection。
5. 重跑 helper tests、單獨測試與之前的受控並行重現；確認不再有 false positive 且沒有殘留 process。
6. [x] 執行相關 test project、完整 solution test、Release build、encoding／CRLF、`git diff --check`；受控並行重現確認 ChurchReport 因 shared lease 等待後通過，且結束後 `WorkerProcessesAfter=0`。詳細輸出見 `check-progress-2026-08-12.md`。
7. [x] 以專案 CCG entrypoint 執行 bounded dual-model review；每次最多等待 45 秒。review run 在期限內沒有可用輸出，已記錄「雙模型未完成」並採本機驗證；詳見 `check-progress-2026-08-12.md` 與 CCG `review.md`。
