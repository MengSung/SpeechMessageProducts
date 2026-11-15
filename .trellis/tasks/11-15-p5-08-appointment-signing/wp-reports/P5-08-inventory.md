# P5-08 預約簽核舊入口盤點

- 盤點日期：2026-11-15 Asia/Taipei (UTC+08:00)
- 基準：`reading`；舊程式不在任何 D365 組織執行，以下只引用工作樹外的退役原始碼。

## 入口與觸發

| 入口 | 舊檔與行號 | 觸發／深度 | 主要呼叫 |
|---|---|---|---|
| `AppointmentPlugIn` | `..\\..\\.其他解決方案\\Dynamics365Line\\AppointmentPlugIn\\AppointmentPlugIn.cs:16-39` | Plugin；`Depth > 1` 返回；建立 `AppointmentSigningLibrary` | `ExecutePlugin` |
| `AppointmentSigningWorkflow` | `..\\..\\.其他解決方案\\Dynamics365Line\\AppointmentSigning\\AppointmentSigningWorkflow.cs:18-50` | CodeActivity；`Depth > 1` 返回；ExecuteWorkflow 分支以 static locker 序列化 | `AppointmentSigningWorkflowClass.Execute` |
| `AppointmentWorkflowPlugin` | `..\\..\\.其他解決方案\\Dynamics365Line\\AppointmentWorkflow\\AppointmentWorkflowPlugin.cs:15-67` | CodeActivity；`Depth > 1` 返回；Create／Update／ExecuteWorkflow 都轉 `AppointmentSigningLibrary` | `AppointmentSigningLibrary.ExecuteWorkflow` |

## 欄位與資料流程

舊 `AppointmentSigningClass` 於 `:27-55、80-254、576-676` 使用：

- `new_signing_contact`：簽核者及通知收件者的 appointment lookup。
- `new_signing_content`：Flex 內文；請假情境再附舊程式查出的休假摘要。
- `new_leave_kind`／`new_location_kind`：決定「請假簽核」或「場地或資源簽核」；兩者同時缺少或同時存在時為「一般簽核」且不通知。
- `scheduledstart`／`scheduledend`／`description`：舊 `SetContent` 會組合申請人、類型、時間、描述；新版保留來源欄位，先以固定純值組合。
- `requiredattendees`／`optionalattendees`：舊程式逐一讀 `partyid` 並再查 contact，組合申請人顯示文字與 LINE ID；新版不以 LINE ID 作為權限或穩定身份。
- `new_applier_appointment`：舊休假統計查詢的申請人 lookup；P5-08 不把休假查詢擴成新的副作用，未來另依核定範圍處理。

## 通知與安全差異

- 舊 Plugin／活動讀 `new_channel_access_token` 並把空值交給 `LineUtilityClass`；依 D5-18／修訂 2，這是退役寫死 token 入口，不能執行第一輪，也不能由新程式讀取。
- 舊程式通知使用 LINE Flex，標題為「有一個簽核的動作等待您的決定....」，按鈕為「同意／退回」，Postback 含 `訊息類別=簽核`、`動作`、正式 appointment ID 與簽核者顯示名稱。
- 舊程式在簽核者無 LINE ID 或 lookup 缺失時錯誤地回落至寫死的開發者 LINE ID；新版 fail-closed，收件者必須是 appointment 的正式 `new_signing_contact` 且由共用服務重新驗證同教會與綁定。
- 例外：舊入口使用 `throw e` 或部分路徑吞錯；新版保留原始例外並由外殼固定追蹤，不建立第二套通知器。

## 新版預計基準與驗證

- 舊行為基準為 `reading`，因退役程式含寫死 token 入口，不能在 D365 執行舊程式。
- 新版 Shadow 只在收件者／教會／語系／訊息組合完成後記錄推播意圖；Live 在安全金鑰查詢前依 P5-PROP-006 fail-closed。
- 本模組不啟用舊步驟、不啟用新 Live 步驟；離線 scenarios／expected 只證明規則與影子投影，不宣稱實機送達或舊程式實跑。
