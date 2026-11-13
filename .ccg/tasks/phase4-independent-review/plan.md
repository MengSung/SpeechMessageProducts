# 審查計畫

1. 讀取 3.5 計畫與 04A，萃取每包明確驗收條件和禁止事項。
2. 以指定對話建立授權、失敗、修正、重試、結案和推送的時間線。
3. 核對 Git 起點、提交、產品 diff、最終 HEAD 和遠端 SHA。
4. 審閱金額／幣別、Sinopac wire format、付款／回呼、冪等、例外和清理等高風險程式與測試。
5. 逐包對照 WP 回報、TRX、guard log、雜湊、階段總報告和進度頁；不能重現的證據明確降級。
6. 執行雙模型並行獨立分析與審查，經專案自動恢復入口；只有原始 run artifacts 能證明後端是否完成。
7. 交叉整理 findings-first 報告，清楚分開可驗證事實、Codex 自述、未驗證條件與政策／文件差異。
8. 執行適用文件檢查，確認本任務以外的程式和階段歷史未變，更新任務狀態並封存。

## 驗證

- `git status --short --branch`、`git rev-parse HEAD`、`git ls-remote origin refs/heads/1.0.0.11.Integration.P4.Worktree`
- `git log --oneline --decorate`、指定提交 `git show` 與必要路徑 `git diff`
- 對實際存在的證據檔執行 `Get-FileHash -Algorithm SHA256`
- `python tools/RepoQuality/check_file_documentation.py`

## 回滾與安全

審查不改產品程式，不更改原階段文件或證據；只在本任務資料夾保存本次規劃、審查結果與摘要。遇到秘密時不記錄其值，只記錄存在位置與處置建議。
