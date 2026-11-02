# P0-01 Worktree 建立證據

## 起點
- 原 worktree：D:/音訊科技產品/系統平台/SpeechMessageProducts/.worktrees/1.0.0.10.IntegrateWebhook_SDK.worktree
- 起點提交：73b6e6cf409daa7559fd3c11d482e3efc542c0d7。
- 新分支：1.0.0.11.Integration.P0。
- 新 worktree：原 worktree 底下 .worktrees/1.0.0.11.Integration.P0.Worktree。
- 先以 git check-ignore 確認 .worktrees/ 由既有 .gitignore 第 267 行排除。

## 首次建立
依使用者指定命令執行 git worktree add，建立了新分支，但 checkout 因 Windows Git 長路徑未啟用而失敗。
代表錯誤：
```text
error: unable to create file .ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/claude-reviewer-attempt-1.prompt.md: Filename too long
fatal: Could not reset index file to revision 'HEAD'.
```

## 原因確認與重試
確認 core.longpaths 未設定、分支已指向正確起點、失敗 checkout 沒有保留 worktree。
僅以單次命令設定 core.longpaths=true，使用已建立的分支重試：
```powershell
git -c core.longpaths=true worktree add '.worktrees\1.0.0.11.Integration.P0.Worktree' 1.0.0.11.Integration.P0
```
命令 exit 0；HEAD 為 73b6e6cf4。
後續 Git 檢查同樣以 -c core.longpaths=true 執行，確保長路徑檔案可正確比對。
這是環境設定修復，與 TDD 的紅／綠燈證據不同。
