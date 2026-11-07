# WeeklyReports

本模組是週報統計、產生與通知規則的預定位置；P2-02 尚未搬移或實作任何舊行為。

## 階段 5 搬移範圍

- P5-05：舊 `WeeklyReportPlugIn`、`WeeklyReportLinePlugIn`、`WeeklyReportWorkflow`。
- 新外殼包含 `WeeklyReportStatisticsPlugin`、`WeeklyReportNotificationPlugin` 與 `WeeklyReportGenerationActivity`；規則集中於本模組。
- P6-02：ChurchReport 週報計算改呼叫此核心，並以舊結果黃金測試維持行為。

搬移須先盤點觸發、欄位及副本差異，再建立至少三種黃金情境。查詢委派 `ToolUtility.Core`，通知委派 `Notifications/MemberNotificationService`；本目錄說明不表示週報已可執行。
