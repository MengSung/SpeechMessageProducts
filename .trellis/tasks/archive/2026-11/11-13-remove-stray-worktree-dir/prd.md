# 刪除異常工作樹容器目錄

## Goal

移除指定且確認為空、未註冊的殘留 worktree 容器目錄；保留正式註冊的 P4 worktree。

## Requirements

- 移除 `D:\音訊科技產品\系統平台\SpeechMessageProducts.IntegrateWebhook_SDK.worktree` 這個殘留容器目錄。
- 執行前確認該路徑不是 Git worktree，並確認目錄樹中沒有檔案、`.git` 管理資料或 reparse point。
- 不影響正式註冊於主要 repository 的 `1.0.0.11.Integration.P4.Worktree`。

## Acceptance Criteria

- [x] 指定目錄已移除，且該路徑不存在。
- [x] 主要 repository 的 Git worktree 清單仍保留正式 P4 worktree。
- [x] 移除前後均未刪除任何檔案或 Git 工作成果。

## Notes

- 目標目錄及其空目錄樹已確認沒有檔案、`.git` 或 reparse point；正式 P4 worktree 位於不同路徑且仍列在 Git worktree 清單中。
