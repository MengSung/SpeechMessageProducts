# WeeklyReports

本模組提供 P5-05 雙目標週報統計、產生及通知純規則；D365 外殼仍負責授權、查詢、旗標修補與持久化。

## 階段 5 搬移範圍

- P5-05：舊 `WeeklyReportPlugIn`、`WeeklyReportLinePlugIn`、`WeeklyReportWorkflow`。
- 新外殼包含 `WeeklyReportStatisticsPlugin`、`WeeklyReportNotificationPlugin` 與 `WeeklyReportGenerationActivity`；規則集中於本模組。
- P6-02：ChurchReport 只共用 `IsCountedClassification` 的分類規則與 `CalculationPrefix()` 的固定 zh-TW 旗標原文；不將依登入者即時建立流程改成整批產生服務。三個沒有編譯呼叫者的舊分類方法移除，原分類顯示與所有建立流程保留；原階段 5 黃金期望不變。

觸發、欄位與差異由階段 5 黃金及雙目標測試保護，查詢委派 `ToolUtility.Core`，
通知委派 `Notifications/MemberNotificationService`。此導讀與離線結果不是頁面或 D365 實機證據，
實際驗收狀態以各工作包回報及階段頁面紀錄為準。
