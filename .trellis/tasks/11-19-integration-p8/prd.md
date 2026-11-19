# 實作計畫 3.5 階段 8 專案終點

## Goal

依階段 8 唯一執行清單 `文件/規劃設計書/實作計畫-3.5/08A_階段8_執行細則.md`，由起點 `6a37f16ba111943eb023c14dea3c9a006a0f0a5c` 按固定順序完成 P8-00、P8-01～P8-05、P7 遺留 L1～L3、整體驗收、ClaudeOnly 階段審查及專案終點回報。

## Requirements

- 全程不連任何正式組織；實機僅允許 `goodshepherdback`，且每個實機單元結束須回到 `off`、`verify=0`、無殘留。
- 依 08A §0.3 固定順序逐一處理單元；每單元依指定測試名稱執行紅燈、實作及綠燈，完成繁中檔案說明、提交、完整守門、一頁回報、驗收、推送及遠端 SHA 核對；未滿足驗收條件者如實保持進行中並依 D8-26 繼續後續單元。
- 不推 `main`、不開 PR、不改寫已推送歷史、不跳過 hook 或守門、不降低檢查、不重佈署 IIS／Node 閘道、不改系統設定、不碰 `C:\smv\guard`。
- 秘密僅由 User Secrets 或環境變數取得；秘密值不可出現在 repo、命令列、紀錄、回報或截圖。
- 進度頁只能修改 DATA；不得改 08A 以外的計畫文件或各工作包規劃文字。
- 依 08A §6 完成 84 包結果、整體驗收、階段審查、終點回報、上線前置清單、AGENTS 終點文字及 P8 分支遠端 SHA 核對；完成後停止，不建立階段 9。

## Acceptance Criteria

- [ ] 84 包皆有驗收結果，或依 08A 縮減驗收並登錄提案與縮減內容。
- [ ] 整體驗收表完整；階段審查無未處理 Critical／High，或明確標示審查未取得及自審結果。
- [ ] `wp-reports/P8-終點回報.md`、`wp-reports/P8-上線前置清單.md` 完成；`AGENTS.md` 明確標示計畫終點。
- [ ] P8 分支已推送且 `git ls-remote` SHA 與本機 HEAD 一致；goodshepherdback 最終唯讀狀態為 `off` 且 `verify=0`。
- [ ] 不建立階段 9 任務／分支、不開 PR、不推 `main`。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
