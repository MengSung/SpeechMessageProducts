# Attendance

本模組是 GoodShepherd 出席領域規則的預定位置；P2-02 只建立目錄導讀，尚無產品規則或資料存取實作。

## 階段 5 搬移範圍

- P5-06：由舊 `PresentRecordLinePlugIn` 盤點並重建出席通知規則。
- 規則目標：`PresentRecordNotificationRule`；Plugin 外殼為 `PresentRecordPlugin`，使用 PreImage 並由非同步流程通知。

搬移前須保存正常、邊界及錯誤情境黃金資料；資料查詢委派 `ToolUtility.Core`，通知走 `Notifications/MemberNotificationService`。不得把 Plugin 註冊或外部傳輸責任放入本模組。
