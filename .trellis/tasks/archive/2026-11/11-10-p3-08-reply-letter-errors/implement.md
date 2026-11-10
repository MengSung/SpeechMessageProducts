# P3-08 執行計畫

1. 啟動 Trellis 子任務，確認 P3-08 是唯一 active 工作包；開工時只更新 3.5 `實作進度.html` DATA。
2. 先新增離線紅燈測試：Reply API token／catalog／sender／QueryUserId、letter stable Upsert 與 direction、dispatcher 真 exception 交接；不連 LINE、D365 或 IIS。
3. 修改 `WebhookEnvelope` 與 `WebhookBusinessResult` 的 bounded detached contracts。
4. 擴充 scope resolver 讀取 member culture、church locales/name，註冊 `IMemberCultureResolver`、`ITextCatalog` 及 P3-08 adapters。
5. 實作 scoped Reply sender／可測試 transport、入站 letter recorder，接入 dispatcher；調整既有 P3-06 離線測試的明確替身與 disposition 斷言。
6. 執行相關 net10 測試，再執行 GoodShepherd 雙目標、Localization／I18n、文件說明、UTF-8／CRLF、duplicate 檢查；失敗自行修正重跑。
7. 產生 `.trellis/tasks/11-10-p3-08-reply-letter-errors/wp-reports/P3-08.md` 一頁離線驗收摘要，明確列出沒有實機結果。
8. 在固定 `C:\smv\guard` 對驗收提交跑完整 §8.1／§8.2；若失敗自行診斷修正並重跑，Critical／High 必須為 0。
9. 只在守門通過後標 Trellis done、推送 `1.0.0.11.Integration.P3.Worktree`、以 `git ls-remote` 核對遠端 SHA。
10. 驗收時再次只更新進度頁 DATA，列出 P3-09 前置缺項，保持 P3-09 todo，完成後停止。

## 變更分組

- 第一個程式提交：本計畫涉及的程式、測試、必要資源／組態與 task 規劃內容。
- 驗收提交：只收驗收回報、守門證據、Trellis狀態及進度頁 DATA；不另建規劃型提交。

## 回復點

每次紅燈測試與建置後確認 `git diff`；若設計缺陷，只回復本包未推送的產品變更，保留 P3-03 暫存 worktree 與既有 P3-07 提交。
