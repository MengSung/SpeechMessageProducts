# P4-00 階段 4 開工切換與前置缺項檢查

## 目標

依 04A §5 完成階段 4 開工切換與前置唯讀檢查。本包只處理文件、Trellis 任務與進度頁 DATA，不修改產品程式、不跑完整 guard、不推送分支。

## 範圍與限制

- 起點是 HEAD `43ea38cd`，分支 `1.0.0.11.Integration.P4.Worktree`；04A 納入版控。
- 將 `AGENTS.md`「現行實作計畫唯一依據」最後一段更新為使用者核定的階段 4 授權文字，其餘內容不動。
- 進度頁只改 DATA：切換 branch/head/stage current、公告與 headline、階段 4 摘要及階段 4 前 user action；所有新增或更新文字均使用 `Asia/Taipei (UTC+08:00)` 日期時間。
- 封存已完成的階段 3 父任務及 P3-09 子任務，不處理早期 `11-02-integration-v32` 任務。
- 在階段父任務 `preflight.md` 留下 00 §7.3 與 04A §5 的唯讀檢查結果；只記秘密鍵名及存在性，不讀取、不輸出秘密值。
- 不重新部署 `LineWebhookP309`、不啟動本機 LineWebhook、不對 D365 寫入、不接觸正式商店或進行付款。

## 驗收條件

- [x] `AGENTS.md` 與 3.5 進度頁 DATA 一致指向階段 4；04A 已納入版控。
- [x] 階段父任務 `preflight.md` 存在，區分已驗證與未驗證，並記錄缺項的後續處理；秘密值未進入任何持久化文字。
- [x] 本子任務 `wp-reports/P4-00.md` 已按 90 附錄 F 提供一頁回報，且進度頁已更新。
- [x] 以下三項 00 §8.3 輕量檢查通過：
  - `powershell -NoProfile -File .\tools\RepoQuality\Test-TextEncoding.ps1 -Scope Changed -Base 43ea38cd`
  - `python .\tools\RepoQuality\check_file_documentation.py`
  - `node --test .\tools\RepoQuality\Tests\Test-ProgressPage.cjs`
- [x] 提交訊息為 `P4-00 chore: 階段 4 開工切換與前置缺項檢查`；不跑完整 §8.1、不推送。
