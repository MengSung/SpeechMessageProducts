# P5-10 關懷活動與規則

## 範圍

依 05A §5 P5-10 與修訂 2，承接三個退役活動入口：`NewPersonCareWorkflow.NewPersonCareWorkflowClass`、`HappyBirthdayWorkflow.HappyBirthdayWorkflowPlugIn`、`RecursiveWorkflow.RecursiveWorkflowPlugIn`。建立 `NewPersonCareActivity`、`ContactAnniversaryActivity` 及 `GoodShepherd.Core/Care/` 規則；不執行舊 DLL，不啟用舊或新 Live 步驟。

## 驗收清單

- [x] 逐入口 reading 盤點：欄位寫入、教會時區、生日／週年、推播分支、遞迴呼叫及失敗行為。
- [x] 建立 3～6 個 scenarios／expected；基準標 `reading` 或 `simulation`，不宣稱實機影子差異。
- [x] 具名測試：`Care_Anniversary_TodayUsesChurchTimeZone_AcrossUtcMidnight`、`Care_Anniversary_Feb29_HandledLikeLegacy`、`Care_Anniversary_SameDayRerun_NoDuplicateWriteOrPush`、`Care_NewPerson_MatchesLegacy`，及每模組固定測試。
- [x] 推播分支遵守 P5-PROP-006：Live 查詢前拒絕，Shadow 完整記錄意圖；Retry Key 使用教會當地日期與會友 ID 的 deterministic GUID；不讀 token。
- [x] 兩 DLL 打包、goodshepherdback plan/apply/verify=0 且全部步驟停用、固定八項 guard、回報／進度頁、任務封存；驗收後才推送並 ls-remote。
