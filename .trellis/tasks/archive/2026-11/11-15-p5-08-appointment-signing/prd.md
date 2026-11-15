# P5-08 預約簽核

依 05A 修訂 2，重建 appointment 的 Plugin／Workflow 簽核入口與共用規則。舊入口只讀來源作為 reading 基準，絕不在 D365 執行舊程式；新入口只以影子模式驗證，Live 活動沿 P5-PROP-006 在安全金鑰查詢前 fail-closed。

## 範圍

- 盤點 `AppointmentPlugIn`、`AppointmentSigningWorkflow`、`AppointmentWorkflowPlugin` 與 `AppointmentSigningLibrary` 的觸發、深度、欄位、輸入輸出、通知與 `new_channel_access_token` 使用。
- 建立或補齊 `GoodShepherd.Core/Appointments/AppointmentSigningRule`，共用已由 P5-07 建出的簽核邏輯，不複製整份課程簽核程式。
- 建立 `AppointmentPlugin` 與 `AppointmentSigningActivity`；活動保留舊輸入／輸出名稱與型別，增加基底 `ExecutionMode`。
- 新程式不得讀 `new_channel_access_token`；Shadow 只記完整推播意圖，Live 在查詢前拒絕。
- 依固定做法建立 3～6 個 scenarios、reading expected、規則雙目標測試、外殼／CAS 測試、註冊清單與停用部署驗證。

## 驗收

- `Appointment_Signing_ApproveAndReject_MatchLegacy`
- `Appointment_Signing_NotifiesNextSigner`
- `Appointment_TokenNeverReadFromRecordField`
- 固定深度、例外、收件者隔離、未綁定、Retry Key 測試。
- Core net48/net10、Plugins／Workflows、CAS、打包檢查、goodshepherdback plan/apply/verify（步驟停用）及固定 guard 全通過。
- 若 Workflows 部署連續失敗兩次才停止回報；不重啟、不 rollback、不重試追蹤或 shadow。
