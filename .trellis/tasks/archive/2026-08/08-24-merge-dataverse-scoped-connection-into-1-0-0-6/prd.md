# 將 feat/dataverse-scoped-connection 合併回 1.0.0.6.DesignNewArchitector

## Goal

在目標分支 worktree 中合併來源分支，處理並驗證任何衝突，確認合併後工作區與提交狀態。

## Requirements

- 以 `1.0.0.6.DesignNewArchitector` 為目前分支，合併 `feat/dataverse-scoped-connection`。
- 合併前確認來源與目標 worktree 的工作區狀態，避免覆蓋未提交的使用者變更。
- 若發生衝突，停止並保留衝突狀態，回報衝突檔案，不擅自猜測業務邏輯。
- 合併完成後檢查 Git 狀態、合併提交與差異摘要；不執行未被請求的遠端推送。

## Acceptance Criteria

- [ ] 目標分支成功包含來源分支的提交，或清楚回報阻塞原因。
- [ ] 合併後工作區無未預期變更，且無未解決衝突。
- [ ] 完成必要的 Git 驗證並回報合併提交與結果。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
