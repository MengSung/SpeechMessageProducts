# Appointments

本模組是預約簽核領域規則的預定位置；目前僅有本導讀，尚未建立 Plugin、Workflow 或資料操作。

## 階段 5 搬移範圍

- P5-08：盤點舊 `AppointmentPlugIn`、`AppointmentSigning`、`AppointmentWorkflow`、`AppointmentSigningLibrary`。
- 新外殼為 `AppointmentPlugin` 與 `AppointmentSigningActivity`；簽核規則集中於 `AppointmentSigningRule`。

實作前須記錄觸發條件、PreImage／欄位、讀寫及副本差異，並建立正常、邊界、錯誤黃金資料。SDK 資料操作委派 `ToolUtility.Core`；不能把 ChurchReport 行事曆的 Session 狀態帶入雙目標領域核心。
