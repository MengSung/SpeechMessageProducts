# 稽核執行清單

1. [x] 核對工作樹、分支、HEAD、遠端 SHA 與相關提交序列。
2. [x] 讀取現行 3.5 規則及階段 4 工作包完成條件；確認 P4-00 的特殊守門／推送規則。
3. [x] 建立 P4-00～P4-06 矩陣：要求、提交、測試名稱、結果、固定 guard、推送、WP 回報、實機／非實機與狀態。
4. [x] 逐包檢查回報與相關 TRX／log／SHA 證據；未重跑 PaymentsLive。
5. [x] 核對階段審查範圍、審查方式、Critical／High、P4-PROP-005～008、使用者待辦、進度頁和階段結案回報。
6. [x] 對每項給出「已驗證完成／核定條件式完成／未完成／證據不足」判定，並記錄矛盾與剩餘風險。
7. [x] 產生本任務稽核報告 `audit-report.md`；未修改既有階段文件或產品檔案。
8. [x] 以唯讀方式核對本稽核以外沒有工作樹變更，且 HEAD／遠端 SHA 一致。

## 驗證命令

- `git status --short --branch`
- `git rev-parse HEAD`
- `git ls-remote origin refs/heads/1.0.0.11.Integration.P4.Worktree`
- 以 XML parser 唯讀讀取 TRX 的測試名稱、計數、結果與時間；計算報告所引用 evidence 的 SHA-256。

## 不執行

- 不重跑 `Category=PaymentsLive` 或任何會連 D365／永豐的測試。
- 不執行 build、部署、D365 verify/apply 或 User Secrets 寫入。
- 不提交或推送原 P4 分支，不修改既有 P4 回報、進度頁或產品程式。
