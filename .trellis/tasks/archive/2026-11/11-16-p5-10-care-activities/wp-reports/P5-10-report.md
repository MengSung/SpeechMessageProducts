# P5-10 一頁回報

- 驗收時間：2026-11-16 11:22 Asia/Taipei (UTC+08:00)。
- 範圍：三個退役入口完成 reading 盤點；關懷出席、生日／週年與 onboarding 活動接線完成；六案基準為 reading／simulation，未執行舊 DLL，未在 goodshepherdback 啟用舊步驟或新 Live。
- 驗證：Core net48／net10 各 337/337；Plugins 581/581；關懷真 WF、部分信任、A/B/A 隔離與清理測試通過；兩 DLL 打包檢查通過；goodshepherdback plan/apply/verify 成功，所有新步驟停用，舊步驟與舊流程啟用皆 0。
- 固定 guard：Release 建置、Release 全方案測試、Debug tracer、Changed encoding、重複碼、I18n、文件說明、進度頁均通過；本包驗收前已完成。
- 限制與待辦：追蹤管道維持 `live=deferred`，不宣稱實機影子差異為零；P5-PROP-006 依核定缺口保留，Host 推播架構於 3.6、P7 前完成；出席 reading 基準與未實跑原因依 D5-3 保留。
- 外部變更：`.ccg/tasks/p5-03b-ui-activity-probe/.turns.json` 與 `.ccg/tasks/net11-performance-research/` 非本包，未納入。
