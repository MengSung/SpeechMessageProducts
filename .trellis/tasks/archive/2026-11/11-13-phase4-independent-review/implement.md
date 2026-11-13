# 審查執行清單

1. [ ] 固定審查對象：工作樹、分支、階段起點與最終提交；保存當前 Git 狀態。
2. [ ] 讀取 00、04A、04 階段 4、各 P4 子任務與一頁回報、階段總回報和驗收步驟。
3. [ ] 讀取指定對話的分頁內容，抽取所有會改變驗收狀態或風險判定的使用者授權與 Codex 結論。
4. [ ] 核驗 P4-00 至 P4-06 的提交、實際差異、指定測試和保存證據；至少深入審閱金額／幣別及 Sinopac／PaymentsLive 高風險路徑。
5. [ ] 核對進度頁 DATA 的日期時間、工作包狀態、待辦／提案、階段 5 未啟動聲明與文件摘要一致性。
6. [ ] 核對自審及外部雙模型審查狀態。任何外部審查僅依原始 runner artifacts 判定；沒有結果即記為未取得。
7. [ ] 對仍可安全在離線環境確認的高風險主張做必要檢查，不重跑即時付款／Sandbox、不引入 secrets、不修改程式。
8. [ ] 形成 findings-first 報告，依嚴重度列出發現、證據、影響、範圍和建議；分開回答「階段工作包結案」與「功能／環境驗收充分性」。
9. [ ] 以 Git diff 確認本任務只新增規劃與審查報告，沒有產品程式、既有歷史回報或進度 DATA 的意外變更。
10. [ ] 執行適用的文件格式／RepoQuality 檢查並封存審查 task；保留報告和證據索引供後續查閱。

## 驗證命令

- `git status --short --branch`
- `git rev-parse HEAD`
- `git ls-remote origin refs/heads/1.0.0.11.Integration.P4.Worktree`
- `git log --oneline --decorate`
- `Get-FileHash <evidence-file> -Algorithm SHA256`（只對實際保存的檔案）
- `python tools/RepoQuality/check_file_documentation.py`

## 回滾／保護點

- 不改產品程式或原階段文件；若產生的審查文檔需更正，只改本任務資料夾。
- 不呼叫會造成外部副作用的付款、撥款、回呼或 Sandbox 測試。
- 發現秘密時不複製到報告；停止展示其值，只記錄檔案／證據類型及處理建議。
